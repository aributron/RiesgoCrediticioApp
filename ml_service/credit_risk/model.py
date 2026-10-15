"""Pipeline compartido de entrenamiento y futura inferencia; no contiene API."""
import numpy as np
from sklearn.compose import ColumnTransformer
from sklearn.impute import SimpleImputer
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import FunctionTransformer, StandardScaler

from credit_risk.data import FEATURES


def build_pipeline(c: float, max_iter: int = 2000) -> Pipeline:
    # log1p reduce la asimetría del ratio sin usar estadísticas del holdout.
    transformations = ColumnTransformer([
        ("debt_ratio_log1p", FunctionTransformer(np.log1p, feature_names_out="one-to-one"), ["DebtRatio"]),
        ("remaining", "passthrough", [name for name in FEATURES if name != "DebtRatio"]),
    ], remainder="drop")
    return Pipeline([
        ("features", transformations),
        ("imputer", SimpleImputer(strategy="median")),
        ("scaler", StandardScaler()),
        ("classifier", LogisticRegression(C=c, solver="lbfgs", max_iter=max_iter, class_weight=None)),
    ])


def positive_probability(pipeline: Pipeline, frame):
    positive_index = list(pipeline.classes_).index(1)
    return pipeline.predict_proba(frame)[:, positive_index]

