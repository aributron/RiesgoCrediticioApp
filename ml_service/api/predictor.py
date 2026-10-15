"""Carga verificada una vez y predicción sin fit, recalibración ni escritura."""
from importlib.metadata import version
import json
from pathlib import Path
import warnings

import joblib
import numpy as np
import pandas as pd
from pydantic import BaseModel, ConfigDict, Field
from sklearn.exceptions import InconsistentVersionWarning
from sklearn.pipeline import Pipeline

from api.schemas import PredictRequest, PredictResponse
from credit_risk.data import FEATURES, TARGET, sha256_file
from credit_risk.model import positive_probability


class ModelUnavailableError(Exception):
    pass


class PredictionError(Exception):
    pass


class ModelMetadata(BaseModel):
    model_config = ConfigDict(strict=True, hide_input_in_errors=True)
    model_version: str = Field(min_length=1)
    target: str
    positive_class: int
    prediction_horizon_months: int
    target_event: str
    feature_names_in_order: list[str]
    artifact_sha256: str = Field(pattern=r"^[0-9a-f]{64}$")
    dependencies: dict[str, str]


class Predictor:
    def __init__(self, pipeline: Pipeline, metadata: ModelMetadata):
        self.pipeline = pipeline
        self.metadata = metadata

    @classmethod
    def load(cls, artifacts: Path) -> "Predictor":
        model_path = artifacts / "model.joblib"
        metadata_path = artifacts / "metadata.json"
        if not model_path.is_file() or not metadata_path.is_file():
            raise ModelUnavailableError("Falta model.joblib o metadata.json en artifacts. Restaurá los artefactos y reiniciá el servicio.")
        try:
            metadata = ModelMetadata.model_validate(json.loads(metadata_path.read_text(encoding="utf-8")))
            if metadata.feature_names_in_order != FEATURES or metadata.target != TARGET or metadata.positive_class != 1:
                raise ModelUnavailableError("El contrato del modelo no coincide con las variables o el objetivo del servicio.")
            if metadata.prediction_horizon_months != 24 or metadata.target_event != "delinquency_90_days_or_more":
                raise ModelUnavailableError("El objetivo o el horizonte del modelo no es compatible con esta API.")
            if not metadata.model_version.strip():
                raise ModelUnavailableError("La versión del modelo está vacía.")
            if sha256_file(model_path) != metadata.artifact_sha256:
                raise ModelUnavailableError("El archivo del modelo no coincide con el checksum registrado.")
            for dependency in ("scikit-learn", "numpy", "scipy", "pandas", "joblib"):
                if metadata.dependencies.get(dependency) != version(dependency):
                    raise ModelUnavailableError("El entorno no coincide con las versiones del baseline. Instalá requirements-api.lock.txt.")
            with warnings.catch_warnings():
                warnings.simplefilter("error", InconsistentVersionWarning)
                pipeline = joblib.load(model_path)
            if not isinstance(pipeline, Pipeline) or list(pipeline.feature_names_in_) != FEATURES:
                raise ModelUnavailableError("El pipeline no tiene las variables esperadas.")
            if list(pipeline.classes_) != [0, 1]:
                raise ModelUnavailableError("Las clases del pipeline no son compatibles con la clase positiva esperada.")
            return cls(pipeline, metadata)
        except ModelUnavailableError:
            raise
        except Exception:
            # No se expone el contenido de metadata, rutas internas ni excepciones de joblib.
            raise ModelUnavailableError("No se pudo cargar un pipeline y metadata válidos. Revisá los artefactos y reiniciá el servicio.") from None

    def predict(self, request: PredictRequest) -> PredictResponse:
        try:
            # Mapeo explícito: solo cuatro predictores, con nombres y orden del entrenamiento.
            frame = pd.DataFrame([[
                request.ageYears,
                request.debtRatio,
                request.latePayments30To59DaysLast24Months,
                request.latePayments60To89DaysLast24Months,
            ]], columns=FEATURES)
            probability = float(positive_probability(self.pipeline, frame)[0])
            if not np.isfinite(probability) or not 0 <= probability <= 1:
                raise PredictionError("El modelo devolvió una probabilidad inválida.")
            return PredictResponse(
                defaultProbability=probability,
                modelVersion=self.metadata.model_version,
                predictionHorizonMonths=self.metadata.prediction_horizon_months,
                targetEvent=self.metadata.target_event,
            )
        except PredictionError:
            raise
        except Exception:
            raise PredictionError("No se pudo obtener una predicción válida del modelo.") from None

