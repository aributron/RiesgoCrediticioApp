"""Entrena y evalúa el baseline; ejecución offline tras descargar el dataset."""
import argparse
from datetime import datetime, timezone
import hashlib
from importlib.metadata import distributions
import json
import os
from pathlib import Path
import platform
import warnings

ROOT = Path(__file__).resolve().parents[1]
os.environ.setdefault("MPLCONFIGDIR", str(ROOT / ".cache" / "matplotlib"))

import joblib
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from sklearn.exceptions import ConvergenceWarning
from sklearn.metrics import log_loss
from sklearn.model_selection import train_test_split

from credit_risk.data import FEATURES, TARGET, load_dataset, prepare_dataset, sha256_file
from credit_risk.evaluation import calibration_summary, calibration_table, metrics
from credit_risk.model import build_pipeline, positive_probability


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False, allow_nan=False), encoding="utf-8")


def split_dataset(x, y, config):
    fractions = [config[name] for name in ("train_fraction", "validation_fraction", "test_fraction")]
    if not np.isclose(sum(fractions), 1.0) or min(fractions) <= 0:
        raise ValueError("Las proporciones deben ser positivas y sumar 1.")
    x_train, x_rest, y_train, y_rest = train_test_split(
        x, y, test_size=fractions[1] + fractions[2],
        stratify=y, random_state=config["random_state"])
    x_valid, x_test, y_valid, y_test = train_test_split(
        x_rest, y_rest, test_size=fractions[2] / (fractions[1] + fractions[2]),
        stratify=y_rest, random_state=config["random_state"])
    return {
        "train": (x_train, y_train), "validation": (x_valid, y_valid), "test": (x_test, y_test)
    }


def plot_calibration(results, probabilities, output):
    fig, axes = plt.subplots(2, 2, figsize=(11, 8))
    for column, name in enumerate(("validation", "test")):
        table = results[name]["table"]
        predicted = table["mean_predicted_probability"]
        observed = table["observed_positive_rate"]
        axes[0, column].plot([0, 1], [0, 1], "--", color="gray", label="Ideal")
        axes[0, column].errorbar(
            predicted, observed,
            yerr=[observed - table["observed_wilson_lower_95"], table["observed_wilson_upper_95"] - observed],
            fmt="o-", capsize=3, label="Logistic regression")
        axes[0, column].set(xlabel="Mean predicted probability", ylabel="Observed event rate",
                            title=f"{name.title()} — quantile bins", xlim=(0, 1), ylim=(0, 1))
        axes[0, column].legend()
        axes[1, column].hist(probabilities[name], bins=np.linspace(0, 1, 31))
        axes[1, column].set(xlabel="Predicted probability", ylabel="Records")
    fig.suptitle("Severe delinquency at 24 months — calibration diagnostic (95% Wilson intervals)")
    fig.tight_layout()
    fig.savefig(output / "calibration.png", dpi=180)
    fig.savefig(output / "calibration.svg")
    plt.close(fig)


def train(data_path: Path, config_path: Path, output: Path):
    config = json.loads(config_path.read_text(encoding="utf-8-sig"))
    frame = load_dataset(data_path)
    x, y, analysis, exclusions = prepare_dataset(frame)
    splits = split_dataset(x, y, config)
    reports = output / "reports"
    artifacts = output / "artifacts"
    reports.mkdir(parents=True, exist_ok=True)
    artifacts.mkdir(parents=True, exist_ok=True)

    x_train, y_train = splits["train"]
    x_valid, y_valid = splits["validation"]
    candidates = []
    winner = None
    best_loss = float("inf")
    # Búsqueda predefinida: el conjunto de prueba no interviene en la selección.
    for c in config["candidate_C"]:
        pipeline = build_pipeline(c, config["max_iter"])
        with warnings.catch_warnings():
            warnings.simplefilter("error", ConvergenceWarning)
            pipeline.fit(x_train, y_train)
        loss = float(log_loss(y_valid, positive_probability(pipeline, x_valid), labels=[0, 1]))
        candidates.append({"C": c, "validation_log_loss": loss})
        if loss < best_loss:
            best_loss, winner = loss, pipeline
    if winner is None:
        raise ValueError("No se entrenó ningún candidato.")

    train_rate = float(y_train.mean())
    evaluations, calibration_results, probabilities = {}, {}, {}
    split_rows = []
    # El modelo seleccionado se evalúa en prueba una vez, sin ajustes posteriores.
    for name, (features, target) in splits.items():
        split_rows.extend({"source_row": int(index), "split": name, "target": int(target.loc[index])}
                          for index in features.index)
        analysis.setdefault("splits", {})[name] = {
            "records": len(target), "positive_records": int(target.sum()),
            "positive_rate": float(target.mean())
        }
        if name == "train":
            continue
        probability = positive_probability(winner, features)
        probabilities[name] = probability
        dummy = np.full(len(target), train_rate)
        evaluations[name] = {
            "logistic_regression": metrics(target, probability, config["diagnostic_thresholds"]),
            "constant_baseline": metrics(target, dummy, config["diagnostic_thresholds"]),
        }
        table = calibration_table(target, probability, config["calibration_bins"])
        calibration_results[name] = {
            "summary": calibration_summary(target, probability, table), "table": table
        }
        table.to_csv(reports / f"calibration_{name}_quantile.csv", index=False)
        calibration_table(target, probability, config["calibration_bins"], "uniform").to_csv(
            reports / f"calibration_{name}_uniform.csv", index=False)
        pd.DataFrame({
            "source_row": features.index, "target": target.to_numpy(),
            "default_probability": probability, "constant_baseline_probability": dummy
        }).to_csv(reports / f"{name}_predictions.csv", index=False)

    model_path = artifacts / "model.joblib"
    joblib.dump(winner, model_path, compress=3)
    loaded = joblib.load(model_path)
    serialization_matches = all(
        np.array_equal(positive_probability(loaded, splits[name][0]), probabilities[name])
        for name in ("validation", "test"))
    if not serialization_matches:
        raise RuntimeError("La recarga del pipeline cambió las probabilidades.")

    versions = dict(sorted((distribution.metadata["Name"], distribution.version) for distribution in distributions()))
    (output / "requirements.lock.txt").write_text(
        "\n".join(f"{name}=={version}" for name, version in versions.items()) + "\n", encoding="utf-8")
    source_files = [
        *sorted((ROOT / "credit_risk").glob("*.py")), *sorted((ROOT / "training").glob("*.py")),
        config_path, ROOT / "requirements.txt",
    ]
    metadata = {
        "model_version": config["model_version"],
        "created_at_utc": datetime.now(timezone.utc).isoformat(),
        "prediction_description": "Probabilidad estimada de morosidad grave a 24 meses",
        "target": TARGET, "positive_class": 1, "prediction_horizon_months": 24,
        "target_event": "delinquency_90_days_or_more",
        "dataset": {
            "name": "Give Me Some Credit", "openml_id": 45577, "openml_version": 1,
            "original_source": "https://www.kaggle.com/competitions/GiveMeSomeCredit",
            "mirror_metadata": "https://www.openml.org/api/v1/json/data/45577",
            "local_filename": data_path.name, "sha256": sha256_file(data_path),
        },
        "feature_names_in_order": FEATURES,
        "feature_semantics": {
            "age": "Edad en años enteros, mínimo 18",
            "DebtRatio": "Compromisos mensuales (deudas, obligaciones alimentarias y gastos de vida) / ingreso bruto mensual; fracción, no porcentaje",
            FEATURES[2]: "Cantidad de atrasos de 30–59 días, sin empeorar, durante los últimos 24 meses",
            FEATURES[3]: "Cantidad de atrasos de 60–89 días, sin empeorar, durante los últimos 24 meses",
        },
        "not_used_as_predictors": ["MonthlyIncome", "requestedAmount", "termMonths", "employmentSeniorityMonths"],
        "preprocessing": [
            "Selección fija de población elegible antes de partir; detalles en dataset_analysis.json",
            "log1p(DebtRatio); las otras tres variables se conservan",
            "SimpleImputer(median), ajustado exclusivamente con entrenamiento",
            "StandardScaler, ajustado exclusivamente con entrenamiento",
            "LogisticRegression regularizada L2; sin sobremuestreo ni class_weight",
        ],
        "selection": {
            "metric": "validation_log_loss", "candidates": candidates,
            "selected_C": float(winner["classifier"].C),
            "fitted_population": "train only; no refit on validation or test",
            "test_used_for_selection": False,
        },
        "config": config, "split_counts": analysis["splits"],
        "training_positive_rate": train_rate,
        "calibration": "Diagnóstico de baseline sin recalibración. Validación se utiliza para seleccionar C; prueba es holdout.",
        "python_version": platform.python_version(), "dependencies": versions,
        "source_files_sha256": {path.relative_to(ROOT).as_posix(): sha256_file(path) for path in source_files},
        "artifact_sha256": sha256_file(model_path),
        "serialization_roundtrip_identical": serialization_matches,
        "limitations": [
            "Dataset histórico externo; no valida probabilidades para Argentina actual.",
            "No permite seleccionar exclusivamente empleados en relación de dependencia.",
            "La población se restringe a ingreso conocido positivo y excluye registros anómalos/duplicados; puede producir sesgo de selección.",
            "Códigos de mora 96/98 excluidos como extremos recurrentes, sin afirmar una semántica de faltante confirmada.",
            "Sin fecha de observación ni ID de persona para separación temporal o por solicitante.",
            "Historial de mora declarado solo para la futura demostración académica; en un entorno real requiere fuentes crediticias externas.",
            "Monto, plazo y antigüedad no influyen en este baseline.",
            "Las bandas de riesgo serán reglas configurables de C#, no decisiones automáticas.",
        ],
    }
    write_json(artifacts / "metadata.json", metadata)
    write_json(reports / "dataset_analysis.json", analysis)
    write_json(reports / "metrics.json", evaluations)
    write_json(reports / "calibration_summary.json",
               {name: result["summary"] for name, result in calibration_results.items()})
    exclusions.to_csv(reports / "excluded_rows.csv", index=False)
    pd.DataFrame(split_rows).sort_values("source_row").to_csv(reports / "split_assignments.csv", index=False)
    plot_calibration(calibration_results, probabilities, reports)
    pd.DataFrame({
        "transformed_feature": winner["features"].get_feature_names_out(),
        "coefficient_after_scaling": winner["classifier"].coef_[0],
    }).to_csv(reports / "coefficients.csv", index=False)
    print(json.dumps({
        "used": analysis["used_records"], "excluded": analysis["excluded_records"],
        "positive_rate": analysis["positive_rate"], "selected_C": metadata["selection"]["selected_C"],
        "metrics": {name: result["logistic_regression"] for name, result in evaluations.items()},
        "calibration": {name: result["summary"] for name, result in calibration_results.items()},
    }, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, default=ROOT / "data" / "raw" / "Give-Me-Some-Credit.arff")
    parser.add_argument("--config", type=Path, default=ROOT / "training" / "config.json")
    parser.add_argument("--output", type=Path, default=ROOT)
    args = parser.parse_args()
    train(args.data.resolve(), args.config.resolve(), args.output.resolve())

