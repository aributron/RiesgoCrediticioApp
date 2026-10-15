import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import joblib
import numpy as np
import pandas as pd

from credit_risk.data import FEATURES, TARGET, load_dataset, prepare_dataset, sha256_file
from credit_risk.evaluation import calibration_summary, calibration_table, metrics
from credit_risk.model import build_pipeline, positive_probability
from training.train import split_dataset

ROOT = Path(__file__).resolve().parents[1]


def sample_frame(size=200):
    rng = np.random.default_rng(17)
    return pd.DataFrame({
        "age": rng.integers(21, 75, size),
        "DebtRatio": rng.uniform(0, 2, size),
        FEATURES[2]: rng.integers(0, 4, size),
        FEATURES[3]: rng.integers(0, 2, size),
        "MonthlyIncome": rng.uniform(1000, 9000, size),
        TARGET: np.tile([0, 0, 0, 1], size // 4),
    })


class PreparationTests(unittest.TestCase):
    def test_exclusion_accounting_and_nonoverlapping_reasons(self):
        frame = sample_frame(20)
        frame[FEATURES[2]] = frame[FEATURES[2]].astype(float)
        frame.loc[0, "MonthlyIncome"] = np.nan
        frame.loc[1, "MonthlyIncome"] = 0
        frame.loc[2, "age"] = 17
        frame.loc[3, "DebtRatio"] = -0.1
        frame.loc[4, FEATURES[2]] = 96
        frame.loc[5, FEATURES[3]] = 98
        frame.loc[6, FEATURES[2]] = 0.5
        frame.loc[7, TARGET] = 7
        frame.loc[20] = frame.loc[8]
        x, y, report, excluded = prepare_dataset(frame)
        self.assertEqual(report["used_records"] + report["excluded_records"], len(frame))
        self.assertEqual(report["excluded_records"], 9)
        self.assertEqual(excluded["source_row"].nunique(), report["excluded_records"])
        self.assertTrue(set(x.index).isdisjoint(set(excluded["source_row"])))
        self.assertEqual(list(x), FEATURES)
        self.assertNotIn("MonthlyIncome", x)
        self.assertEqual(set(y.unique()), {0, 1})

    def test_never_changes_target_or_valid_debt_ratios(self):
        frame = sample_frame()
        frame.loc[0, "DebtRatio"] = 200
        x, y, _, _ = prepare_dataset(frame)
        self.assertEqual(x.loc[0, "DebtRatio"], 200)
        pd.testing.assert_series_equal(y, frame[TARGET].astype(int))

    def test_insufficient_classes_rejected(self):
        frame = sample_frame()
        frame[TARGET] = 0
        with self.assertRaises(ValueError):
            prepare_dataset(frame)

    def test_missing_columns_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "bad.csv"
            path.write_text("age,DebtRatio\n35,0.5\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                load_dataset(path)


class SplitAndPipelineTests(unittest.TestCase):
    def setUp(self):
        self.x, self.y, _, _ = prepare_dataset(sample_frame(400))
        self.config = json.loads((ROOT / "training" / "config.json").read_text(encoding="utf-8-sig"))

    def test_splits_disjoint_complete_reproducible_and_stratified(self):
        first = split_dataset(self.x, self.y, self.config)
        second = split_dataset(self.x, self.y, self.config)
        indexes = [set(pair[0].index) for pair in first.values()]
        self.assertEqual(set.union(*indexes), set(self.x.index))
        for i in range(len(indexes)):
            for j in range(i):
                self.assertTrue(indexes[i].isdisjoint(indexes[j]))
        for name in first:
            self.assertEqual(list(first[name][0].index), list(second[name][0].index))
            self.assertLess(abs(first[name][1].mean() - self.y.mean()), 0.02)

    def test_preprocessing_fitted_only_on_training(self):
        splits = split_dataset(self.x, self.y, self.config)
        x_train, y_train = splits["train"]
        pipeline = build_pipeline(0.1).fit(x_train, y_train)
        expected = pipeline["imputer"].transform(pipeline["features"].transform(x_train)).mean(axis=0)
        np.testing.assert_allclose(pipeline["scaler"].mean_, expected)
        before = pipeline["scaler"].mean_.copy()
        holdout = splits["test"][0].copy()
        holdout["age"] = 95
        positive_probability(pipeline, holdout)
        np.testing.assert_array_equal(pipeline["scaler"].mean_, before)

    def test_probability_and_joblib_roundtrip(self):
        pipeline = build_pipeline(0.1).fit(self.x, self.y)
        probability = positive_probability(pipeline, self.x)
        self.assertTrue(np.isfinite(probability).all())
        self.assertTrue(((probability >= 0) & (probability <= 1)).all())
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "model.joblib"
            joblib.dump(pipeline, path)
            np.testing.assert_array_equal(positive_probability(joblib.load(path), self.x), probability)

    def test_nominal_income_cannot_influence_model(self):
        pipeline = build_pipeline(0.1).fit(self.x, self.y)
        first = self.x.assign(MonthlyIncome=1000)
        second = self.x.assign(MonthlyIncome=1000000)
        np.testing.assert_array_equal(
            positive_probability(pipeline, first), positive_probability(pipeline, second))


class EvaluationTests(unittest.TestCase):
    def test_constant_baseline_and_metric_validation(self):
        y = np.array([0, 0, 0, 1])
        result = metrics(y, np.full(4, 0.25), [0.5])
        self.assertEqual(result["roc_auc"], 0.5)
        self.assertEqual(result["average_precision"], 0.25)
        self.assertEqual(result["brier_score"], 0.1875)
        self.assertEqual(result["classification_diagnostics"]["0.5"]["confusion_matrix_labels_0_1"], [[3, 0], [1, 0]])
        for probability in (np.array([0, 0, 0, 2]), np.array([0, 0, 0, np.nan])):
            with self.assertRaises(ValueError):
                metrics(y, probability, [0.5])

    def test_calibration_tables_cover_all_records(self):
        y = np.tile([0, 0, 1, 0], 50)
        probability = np.linspace(0.01, 0.99, len(y))
        for strategy in ("quantile", "uniform"):
            table = calibration_table(y, probability, strategy=strategy)
            self.assertEqual(table["count"].sum(), len(y))
            self.assertTrue((table["observed_wilson_lower_95"] <= table["observed_positive_rate"]).all())
            self.assertTrue((table["observed_wilson_upper_95"] >= table["observed_positive_rate"]).all())
        summary = calibration_summary(y, probability, table)
        self.assertTrue(np.isfinite(summary["diagnostic_calibration_slope"]))


@unittest.skipUnless((ROOT / "artifacts" / "model.joblib").exists(), "Ejecutar entrenamiento para verificar artefactos.")
class ArtifactTests(unittest.TestCase):
    def test_artifact_metadata_checksums_and_source(self):
        metadata = json.loads((ROOT / "artifacts" / "metadata.json").read_text(encoding="utf-8"))
        self.assertEqual(metadata["artifact_sha256"], sha256_file(ROOT / "artifacts" / "model.joblib"))
        self.assertEqual(metadata["feature_names_in_order"], FEATURES)
        self.assertEqual(metadata["prediction_horizon_months"], 24)
        self.assertFalse(metadata["selection"]["test_used_for_selection"])
        for name, digest in metadata["source_files_sha256"].items():
            self.assertEqual(digest, sha256_file(ROOT / name))
        data = ROOT / "data" / "raw" / metadata["dataset"]["local_filename"]
        self.assertEqual(metadata["dataset"]["sha256"], sha256_file(data))

    def test_saved_holdout_predictions_and_metrics_reproduce(self):
        x, y, _, _ = prepare_dataset(load_dataset(ROOT / "data" / "raw" / "Give-Me-Some-Credit.arff"))
        pipeline = joblib.load(ROOT / "artifacts" / "model.joblib")
        report = json.loads((ROOT / "reports" / "metrics.json").read_text(encoding="utf-8"))
        assignments = pd.read_csv(ROOT / "reports" / "split_assignments.csv")
        self.assertEqual(len(assignments), len(x))
        self.assertEqual(assignments["source_row"].nunique(), len(x))
        for name in ("validation", "test"):
            saved = pd.read_csv(ROOT / "reports" / f"{name}_predictions.csv")
            indexes = saved["source_row"].to_numpy()
            self.assertEqual(set(indexes), set(assignments.loc[assignments["split"] == name, "source_row"]))
            probability = positive_probability(pipeline, x.loc[indexes])
            np.testing.assert_allclose(saved["default_probability"], probability, rtol=1e-12, atol=1e-12)
            actual = metrics(y.loc[indexes], probability, [0.1, 0.25, 0.5])
            for key in ("roc_auc", "average_precision", "brier_score", "log_loss"):
                self.assertAlmostEqual(actual[key], report[name]["logistic_regression"][key], places=12)


if __name__ == "__main__":
    unittest.main()

