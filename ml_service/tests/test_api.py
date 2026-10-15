import contextlib
import io
import json
import logging
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
import urllib.request

from fastapi.testclient import TestClient
import joblib
import numpy as np
import pandas as pd

from api.main import ARTIFACTS, create_app
from credit_risk.data import FEATURES
from credit_risk.model import positive_probability

EXAMPLE = {
    "ageYears": 35,
    "debtRatio": 0.35,
    "latePayments30To59DaysLast24Months": 1,
    "latePayments60To89DaysLast24Months": 0,
}
RESPONSE_FIELDS = {
    "defaultProbability", "modelVersion", "predictionHorizonMonths", "targetEvent"
}


class ApiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.client = TestClient(create_app())
        cls.client.__enter__()

    @classmethod
    def tearDownClass(cls):
        cls.client.__exit__(None, None, None)

    def test_health(self):
        response = self.client.get("/health")
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json(), {
            "status": "ok", "modelAvailable": True, "modelVersion": "gmsc-logreg-v1"
        })

    def test_valid_prediction_contract_and_range(self):
        response = self.client.post("/v1/predict", json=EXAMPLE)
        self.assertEqual(response.status_code, 200)
        body = response.json()
        self.assertEqual(set(body), RESPONSE_FIELDS)
        self.assertTrue(0 <= body["defaultProbability"] <= 1)
        self.assertEqual(body["modelVersion"], "gmsc-logreg-v1")
        self.assertEqual(body["predictionHorizonMonths"], 24)
        self.assertEqual(body["targetEvent"], "delinquency_90_days_or_more")

    def test_same_probability_as_direct_pipeline(self):
        pipeline = joblib.load(ARTIFACTS / "model.joblib")
        frame = pd.DataFrame([[35, 0.35, 1, 0]], columns=FEATURES)
        direct = float(positive_probability(pipeline, frame)[0])
        via_api = self.client.post("/v1/predict", json=EXAMPLE).json()["defaultProbability"]
        self.assertEqual(direct, via_api)

    def test_same_probabilities_for_multiple_profiles(self):
        for values in ([21, 0.0, 0, 0], [50, 1.5, 2, 1], [75, 0.2, 0, 0]):
            with self.subTest(values=values):
                payload = dict(zip(EXAMPLE, values))
                direct = float(positive_probability(
                    self.client.app.state.predictor.pipeline, pd.DataFrame([values], columns=FEATURES))[0])
                response = self.client.post("/v1/predict", json=payload)
                self.assertEqual(response.status_code, 200)
                self.assertEqual(response.json()["defaultProbability"], direct)

    def test_invalid_inputs(self):
        cases = [
            {}, {**EXAMPLE, "ageYears": 17}, {**EXAMPLE, "ageYears": 121},
            {**EXAMPLE, "ageYears": 35.5}, {**EXAMPLE, "ageYears": True},
            {**EXAMPLE, "ageYears": "35"}, {**EXAMPLE, "debtRatio": -0.1},
            {**EXAMPLE, "debtRatio": "0.35"}, {**EXAMPLE, "debtRatio": None},
            {**EXAMPLE, "debtRatio": True},
            {**EXAMPLE, "latePayments30To59DaysLast24Months": -1},
            {**EXAMPLE, "latePayments60To89DaysLast24Months": 1.5},
            {**EXAMPLE, "latePayments60To89DaysLast24Months": False},
            {**EXAMPLE, "latePayments30To59DaysLast24Months": 25},
            {**EXAMPLE, "latePayments60To89DaysLast24Months": 25},
            {**EXAMPLE, "unexpectedField": "private-marker"},
            {**EXAMPLE, "latePayments30To59DaysLast24Months": 2147483648},
        ]
        for payload in cases:
            with self.subTest(payload=payload):
                response = self.client.post("/v1/predict", json=payload)
                self.assertEqual(response.status_code, 422)
                self.assertEqual(response.json()["error"]["code"], "invalid_input")
                self.assertTrue(all("input" not in detail and "ctx" not in detail
                                    for detail in response.json()["error"]["details"]))
                self.assertNotIn("private-marker", response.text)

    def test_missing_each_required_field(self):
        for field in EXAMPLE:
            with self.subTest(field=field):
                payload = EXAMPLE.copy()
                del payload[field]
                self.assertEqual(self.client.post("/v1/predict", json=payload).status_code, 422)

    def test_late_count_boundaries(self):
        for count in (0, 24):
            payload = {**EXAMPLE, "latePayments30To59DaysLast24Months": count,
                       "latePayments60To89DaysLast24Months": count}
            self.assertEqual(self.client.post("/v1/predict", json=payload).status_code, 200)

    def test_nonfinite_inputs(self):
        for value in (float("nan"), float("inf"), -float("inf")):
            with self.subTest(value=value):
                body = json.dumps({**EXAMPLE, "debtRatio": value})
                response = self.client.post("/v1/predict", content=body, headers={"Content-Type": "application/json"})
                self.assertEqual(response.status_code, 422)

    def test_invalid_json_or_non_object(self):
        for body in ('{"ageYears":', 'null', '[]', '"private-marker"'):
            with self.subTest(body=body):
                response = self.client.post("/v1/predict", content=body, headers={"Content-Type": "application/json"})
                self.assertEqual(response.status_code, 422)
                self.assertNotIn("private-marker", response.text)

    def test_model_not_retrained_during_requests(self):
        pipeline = self.client.app.state.predictor.pipeline
        with patch.object(pipeline, "fit", side_effect=AssertionError("No fit allowed")), patch.object(
            pipeline["classifier"], "fit", side_effect=AssertionError("No fit allowed")
        ):
            self.assertEqual(self.client.post("/v1/predict", json=EXAMPLE).status_code, 200)

    def test_invalid_model_probabilities_are_not_returned(self):
        pipeline = self.client.app.state.predictor.pipeline
        for value in (-0.01, 1.01, float("nan"), float("inf")):
            with self.subTest(value=value), patch.object(
                pipeline, "predict_proba", return_value=np.array([[0.5, value]])
            ):
                response = self.client.post("/v1/predict", json=EXAMPLE)
                self.assertEqual(response.status_code, 500)
                self.assertEqual(response.json()["error"]["code"], "prediction_failed")
                self.assertNotIn("defaultProbability", response.json())

    def test_boundary_probabilities_are_valid(self):
        pipeline = self.client.app.state.predictor.pipeline
        for value in (0.0, 1.0):
            with self.subTest(value=value), patch.object(
                pipeline, "predict_proba", return_value=np.array([[1 - value, value]])
            ):
                response = self.client.post("/v1/predict", json=EXAMPLE)
                self.assertEqual(response.status_code, 200)
                self.assertEqual(response.json()["defaultProbability"], value)

    def test_internal_error_does_not_expose_values(self):
        pipeline = self.client.app.state.predictor.pipeline
        with patch.object(pipeline, "predict_proba", side_effect=RuntimeError("private-marker")):
            response = self.client.post("/v1/predict", json=EXAMPLE)
            self.assertEqual(response.status_code, 500)
            self.assertNotIn("private-marker", response.text)

    def test_no_request_bodies_or_personal_data_logged(self):
        output = io.StringIO()
        handler = logging.StreamHandler(output)
        logger = logging.getLogger()
        logger.addHandler(handler)
        try:
            with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
                self.client.post("/v1/predict", json={**EXAMPLE, "name": "private-person-marker"})
                self.client.post("/v1/predict", json=EXAMPLE)
        finally:
            logger.removeHandler(handler)
        self.assertNotIn("private-person-marker", output.getvalue())
        self.assertNotIn("latePayments30To59DaysLast24Months", output.getvalue())

    def test_pipeline_loaded_once_at_startup(self):
        original_load = joblib.load
        with patch("api.predictor.joblib.load", wraps=original_load) as load:
            with TestClient(create_app()) as client:
                self.assertEqual(load.call_count, 1)
                for _ in range(3):
                    self.assertEqual(client.get("/health").status_code, 200)
                    self.assertEqual(client.post("/v1/predict", json=EXAMPLE).status_code, 200)
                self.assertEqual(load.call_count, 1)


class UnavailableModelTests(unittest.TestCase):
    def test_missing_model(self):
        with tempfile.TemporaryDirectory() as directory:
            artifacts = Path(directory)
            shutil.copy(ARTIFACTS / "metadata.json", artifacts)
            with TestClient(create_app(artifacts)) as client:
                health = client.get("/health")
                self.assertEqual(health.status_code, 503)
                self.assertFalse(health.json()["modelAvailable"])
                response = client.post("/v1/predict", json=EXAMPLE)
                self.assertEqual(response.status_code, 503)
                self.assertEqual(response.json()["error"]["code"], "model_unavailable")

    def test_missing_metadata(self):
        with tempfile.TemporaryDirectory() as directory:
            artifacts = Path(directory)
            shutil.copy(ARTIFACTS / "model.joblib", artifacts)
            with TestClient(create_app(artifacts)) as client:
                self.assertEqual(client.get("/health").status_code, 503)

    def test_invalid_metadata_or_incompatible_contract(self):
        for change in (
            {"feature_names_in_order": ["wrong"]},
            {"prediction_horizon_months": 12},
            {"target_event": "wrong"},
            {"positive_class": 0},
            {"model_version": " "},
            {"artifact_sha256": "0" * 64},
            {"dependencies": {}},
        ):
            with self.subTest(change=change), tempfile.TemporaryDirectory() as directory:
                artifacts = Path(directory)
                shutil.copy(ARTIFACTS / "model.joblib", artifacts)
                metadata = json.loads((ARTIFACTS / "metadata.json").read_text(encoding="utf-8"))
                metadata.update(change)
                (artifacts / "metadata.json").write_text(json.dumps(metadata), encoding="utf-8")
                with TestClient(create_app(artifacts)) as client:
                    self.assertEqual(client.get("/health").status_code, 503)
                    self.assertEqual(client.post("/v1/predict", json=EXAMPLE).status_code, 503)

    def test_unreadable_metadata(self):
        with tempfile.TemporaryDirectory() as directory:
            artifacts = Path(directory)
            shutil.copy(ARTIFACTS / "model.joblib", artifacts)
            (artifacts / "metadata.json").write_text("not JSON", encoding="utf-8")
            with TestClient(create_app(artifacts)) as client:
                self.assertEqual(client.get("/health").status_code, 503)

    def test_unpickling_failure(self):
        with patch("api.predictor.joblib.load", side_effect=ValueError("private-marker")):
            with TestClient(create_app()) as client:
                response = client.get("/health")
                self.assertEqual(response.status_code, 503)
                self.assertNotIn("private-marker", response.text)


class UvicornSmokeTests(unittest.TestCase):
    def test_local_server_health_and_prediction_over_http(self):
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            port = listener.getsockname()[1]
        command = [
            sys.executable, "-m", "uvicorn", "api.main:app",
            "--host", "127.0.0.1", "--port", str(port),
            "--workers", "1", "--no-access-log",
        ]
        options = {"creationflags": subprocess.CREATE_NO_WINDOW} if os.name == "nt" else {}
        process = subprocess.Popen(command, cwd=ARTIFACTS.parent, stdout=subprocess.DEVNULL,
                                   stderr=subprocess.DEVNULL, **options)
        try:
            base = f"http://127.0.0.1:{port}"
            deadline = time.monotonic() + 15
            while True:
                try:
                    with urllib.request.urlopen(base + "/health", timeout=1) as response:
                        health = json.load(response)
                    break
                except (OSError, TimeoutError):
                    if process.poll() is not None or time.monotonic() > deadline:
                        self.fail("Uvicorn no inició correctamente en loopback.")
                    time.sleep(0.1)
            self.assertTrue(health["modelAvailable"])
            request = urllib.request.Request(
                base + "/v1/predict", data=json.dumps(EXAMPLE).encode(),
                headers={"Content-Type": "application/json"}, method="POST")
            with urllib.request.urlopen(request, timeout=5) as response:
                body = json.load(response)
            self.assertEqual(set(body), RESPONSE_FIELDS)
            self.assertTrue(0 <= body["defaultProbability"] <= 1)
        finally:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)


if __name__ == "__main__":
    unittest.main()

