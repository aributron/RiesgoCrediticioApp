"""Métricas probabilísticas y diagnóstico de calibración, sin recalibrar el modelo."""
import numpy as np
import pandas as pd
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import (
    average_precision_score, brier_score_loss, confusion_matrix, f1_score,
    log_loss, precision_score, recall_score, roc_auc_score,
)


def metrics(y, probability, thresholds):
    y = np.asarray(y, dtype=int)
    probability = np.asarray(probability, dtype=float)
    if probability.shape != y.shape or not np.isfinite(probability).all() or (
        (probability < 0) | (probability > 1)).any():
        raise ValueError("Probabilidades inválidas.")
    diagnostics = {}
    for threshold in thresholds:
        predicted = (probability >= threshold).astype(int)
        diagnostics[str(threshold)] = {
            "precision": float(precision_score(y, predicted, zero_division=0)),
            "recall": float(recall_score(y, predicted, zero_division=0)),
            "f1": float(f1_score(y, predicted, zero_division=0)),
            "confusion_matrix_labels_0_1": confusion_matrix(y, predicted, labels=[0, 1]).tolist(),
        }
    return {
        "records": len(y), "positive_rate": float(y.mean()),
        "mean_predicted_probability": float(probability.mean()),
        "roc_auc": float(roc_auc_score(y, probability)),
        "average_precision": float(average_precision_score(y, probability)),
        "brier_score": float(brier_score_loss(y, probability)),
        "log_loss": float(log_loss(y, probability, labels=[0, 1])),
        "classification_diagnostics": diagnostics,
    }


def calibration_table(y, probability, bins=10, strategy="quantile"):
    data = pd.DataFrame({"observed": np.asarray(y, dtype=int), "predicted": np.asarray(probability, dtype=float)})
    if strategy == "quantile":
        data["bin"] = pd.qcut(data["predicted"], bins, duplicates="drop")
    elif strategy == "uniform":
        data["bin"] = pd.cut(data["predicted"], np.linspace(0, 1, bins + 1), include_lowest=True)
    else:
        raise ValueError("Estrategia inválida.")
    rows = []
    for interval, group in data.groupby("bin", observed=True):
        n = len(group)
        rate = float(group["observed"].mean())
        denominator = 1 + 1.96 ** 2 / n
        center = (rate + 1.96 ** 2 / (2 * n)) / denominator
        radius = 1.96 * np.sqrt(rate * (1 - rate) / n + 1.96 ** 2 / (4 * n ** 2)) / denominator
        rows.append({
            "bin": str(interval), "count": n,
            "mean_predicted_probability": float(group["predicted"].mean()),
            "observed_positive_rate": rate,
            "observed_wilson_lower_95": max(0.0, float(center - radius)),
            "observed_wilson_upper_95": min(1.0, float(center + radius)),
        })
    return pd.DataFrame(rows)


def calibration_summary(y, probability, table):
    clipped = np.clip(probability, 1e-12, 1 - 1e-12)
    logits = np.log(clipped / (1 - clipped)).reshape(-1, 1)
    diagnostic = LogisticRegression(penalty=None, max_iter=2000).fit(logits, y)
    ece = np.average(
        np.abs(table["mean_predicted_probability"] - table["observed_positive_rate"]),
        weights=table["count"])
    return {
        "observed_positive_rate": float(np.mean(y)),
        "mean_predicted_probability": float(np.mean(probability)),
        "mean_prediction_minus_observed": float(np.mean(probability) - np.mean(y)),
        "ece_quantile_bins": float(ece),
        "diagnostic_calibration_intercept": float(diagnostic.intercept_[0]),
        "diagnostic_calibration_slope": float(diagnostic.coef_[0, 0]),
        "note": "Intercepto ideal 0 y pendiente ideal 1. Ajuste diagnóstico sobre este conjunto; no modifica el pipeline ni se usa para recalibrar. ECE depende del binning.",
    }

