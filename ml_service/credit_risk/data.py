"""Reglas de elegibilidad fijas, independientes de métricas o particiones."""
import hashlib
from pathlib import Path

import numpy as np
import pandas as pd
from scipy.io import arff

TARGET = "SeriousDlqin2yrs"
FEATURES = [
    "age",
    "DebtRatio",
    "NumberOfTime30-59DaysPastDueNotWorse",
    "NumberOfTime60-89DaysPastDueNotWorse",
]
LATE_FEATURES = FEATURES[2:]
# Códigos extremos recurrentes; se excluyen conservadoramente, sin asumir que sean ceros.
ANOMALOUS_LATE_CODES = (96, 98)


def load_dataset(path: Path) -> pd.DataFrame:
    if path.suffix.lower() == ".arff":
        records, _ = arff.loadarff(path)
        frame = pd.DataFrame(records)
    elif path.suffix.lower() == ".csv":
        frame = pd.read_csv(path)
        frame = frame.drop(columns=[c for c in frame if c.startswith("Unnamed:")])
    else:
        raise ValueError("Se requiere un dataset ARFF o CSV.")
    required = [TARGET, *FEATURES, "MonthlyIncome"]
    missing = set(required) - set(frame.columns)
    if missing:
        raise ValueError(f"Faltan columnas: {sorted(missing)}")
    for column in frame:
        if frame[column].dtype == object:
            frame[column] = frame[column].map(lambda value: value.decode() if isinstance(value, bytes) else value)
        frame[column] = pd.to_numeric(frame[column], errors="coerce")
    frame.index.name = "source_row"
    return frame


def prepare_dataset(frame: pd.DataFrame):
    original = frame.copy()
    working = frame.copy()
    exclusions = []
    excluded_rows = []
    rules = [
        ("invalid_target", lambda d: ~d[TARGET].isin([0, 1])),
        ("missing_or_nonpositive_income", lambda d: ~np.isfinite(d["MonthlyIncome"]) | (d["MonthlyIncome"] <= 0)),
        ("invalid_or_underage_age", lambda d: ~np.isfinite(d["age"]) | (d["age"] < 18) | (d["age"] % 1 != 0)),
        ("invalid_debt_ratio", lambda d: ~np.isfinite(d["DebtRatio"]) | (d["DebtRatio"] < 0)),
        ("missing_negative_or_fractional_late_count",
         lambda d: (~np.isfinite(d[LATE_FEATURES]) | (d[LATE_FEATURES] < 0) | (d[LATE_FEATURES] % 1 != 0)).any(axis=1)),
        ("anomalous_late_codes_96_98", lambda d: d[LATE_FEATURES].isin(ANOMALOUS_LATE_CODES).any(axis=1)),
        ("exact_duplicate_raw_record", lambda d: d.duplicated(keep="first")),
    ]
    for name, rule in rules:
        mask = rule(working)
        exclusions.append({"reason": name, "count": int(mask.sum())})
        excluded_rows.extend({"source_row": int(index), "reason": name} for index in working.index[mask])
        working = working.loc[~mask].copy()
    target = working[TARGET].astype(int)
    analysis = {
        "raw_records": len(original),
        "raw_positive_records": int((original[TARGET] == 1).sum()),
        "raw_positive_rate": float((original[TARGET] == 1).mean()),
        "used_records": len(working),
        "excluded_records": len(original) - len(working),
        "positive_records": int(target.sum()),
        "positive_rate": float(target.mean()),
        "exclusions_sequential_nonoverlapping": exclusions,
        "raw_missing_values": {key: int(value) for key, value in original.isna().sum().items()},
        "raw_anomalous_late_code_counts": {
            column: {str(code): int((original[column] == code).sum()) for code in ANOMALOUS_LATE_CODES}
            for column in LATE_FEATURES
        },
        "eligible_feature_summary": working[FEATURES].describe(
            percentiles=[0.01, 0.5, 0.95, 0.99]).to_dict(),
    }
    if len(working) == 0 or target.nunique() != 2:
        raise ValueError("La población elegible debe contener ambas clases.")
    return working[FEATURES], target, analysis, pd.DataFrame(excluded_rows, columns=["source_row", "reason"])


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()

