"""Genera un informe legible y gráficos desde resultados guardados; no entrena ni selecciona."""
import json
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
os.environ.setdefault("MPLCONFIGDIR", str(ROOT / ".cache" / "matplotlib"))
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import pandas as pd
from sklearn.metrics import precision_recall_curve, roc_curve


def main():
    reports = ROOT / "reports"
    analysis = json.loads((reports / "dataset_analysis.json").read_text(encoding="utf-8"))
    metadata = json.loads((ROOT / "artifacts" / "metadata.json").read_text(encoding="utf-8"))
    metrics = json.loads((reports / "metrics.json").read_text(encoding="utf-8"))
    calibration = json.loads((reports / "calibration_summary.json").read_text(encoding="utf-8"))

    fig, axes = plt.subplots(1, 2, figsize=(11, 5))
    for axis, name in zip(axes, ("validation", "test")):
        table = pd.read_csv(reports / f"calibration_{name}_uniform.csv")
        observed = table["observed_positive_rate"]
        axis.plot([0, 1], [0, 1], "--", color="gray", label="Ideal")
        axis.errorbar(table["mean_predicted_probability"], observed,
            yerr=[observed - table["observed_wilson_lower_95"], table["observed_wilson_upper_95"] - observed],
            fmt="o-", capsize=3, label="Logistic regression")
        for _, row in table.iterrows():
            axis.annotate(str(int(row["count"])),
                (row["mean_predicted_probability"], row["observed_positive_rate"]),
                xytext=(3, 5), textcoords="offset points", fontsize=7)
        axis.set(title=name.title(), xlabel="Mean predicted probability",
                 ylabel="Observed event rate", xlim=(0, 1), ylim=(0, 1))
        axis.legend()
    fig.suptitle("Uniform calibration bins — labels: record count; 95% Wilson intervals")
    fig.tight_layout()
    fig.savefig(reports / "calibration_uniform.png", dpi=180)
    fig.savefig(reports / "calibration_uniform.svg")
    plt.close(fig)

    fig, axes = plt.subplots(2, 2, figsize=(11, 8))
    for column, name in enumerate(("validation", "test")):
        predictions = pd.read_csv(reports / f"{name}_predictions.csv")
        target = predictions["target"]
        probability = predictions["default_probability"]
        false_positive, true_positive, _ = roc_curve(target, probability)
        precision, recall, _ = precision_recall_curve(target, probability)
        axes[0, column].plot(false_positive, true_positive)
        axes[0, column].plot([0, 1], [0, 1], "--", color="gray")
        axes[0, column].set(title=f"{name.title()} — ROC", xlabel="False positive rate",
                            ylabel="True positive rate", xlim=(0, 1), ylim=(0, 1))
        axes[1, column].plot(recall, precision)
        axes[1, column].axhline(target.mean(), linestyle="--", color="gray", label="Prevalence")
        axes[1, column].set(title=f"{name.title()} — precision-recall", xlabel="Recall",
                            ylabel="Precision", xlim=(0, 1), ylim=(0, 1))
        axes[1, column].legend()
    fig.tight_layout()
    fig.savefig(reports / "discrimination_curves.png", dpi=180)
    fig.savefig(reports / "discrimination_curves.svg")
    plt.close(fig)

    lines = [
        "# Resultado del baseline GMSC",
        "",
        f"Modelo: **{metadata['model_version']}**. Objetivo: **probabilidad estimada de morosidad grave a 24 meses**.",
        "",
        "## Dataset y preparación",
        "",
        "Fuente original: [Give Me Some Credit](https://www.kaggle.com/competitions/GiveMeSomeCredit).",
        "Copia versionada: [OpenML 45577, versión 1](https://www.openml.org/api/v1/json/data/45577).",
        f"SHA-256 del archivo: `{metadata['dataset']['sha256']}`.",
        "",
        f"- Originales: {analysis['raw_records']:,}; positivos: {analysis['raw_positive_records']:,} ({analysis['raw_positive_rate']:.4%}).",
        f"- Utilizados: {analysis['used_records']:,}; positivos: {analysis['positive_records']:,} ({analysis['positive_rate']:.4%}).",
        f"- Excluidos: {analysis['excluded_records']:,}. Los motivos siguientes son secuenciales y no se superponen.",
        "",
        "| Motivo | Registros |", "|---|---:|",
    ]
    lines += [f"| {entry['reason']} | {entry['count']:,} |" for entry in analysis["exclusions_sequential_nonoverlapping"]]
    lines += ["", "Los códigos 96/98 se excluyeron como extremos recurrentes; su significado como faltante no está confirmado. No se recodificaron como cero.",
              "Los duplicados se identificaron por igualdad de toda la fila original, no solamente por las cuatro variables seleccionadas.",
              "", "## Variables y entrenamiento", "",
              *[f"- `{feature}`" for feature in metadata["feature_names_in_order"]],
              "", "Pipeline: log1p del ratio, imputación mediana, escalado y Regresión Logística L2.",
              "Imputación y escalado ajustados solo con entrenamiento. Tras los filtros no hay faltantes en las cuatro entradas.",
              f"C elegido: **{metadata['selection']['selected_C']}**, por menor log loss de validación entre 0.1, 1 y 10.",
              "La diferencia entre candidatos es mínima; no implica una mejora sustancial demostrada.",
              "Sin SMOTE, balanceo de clases, recalibración ni ajuste posterior al observar prueba.",
              "El artefacto conserva el ajuste sobre entrenamiento: no fue reentrenado con validación ni prueba.",
              "", "| Partición | Registros | Positivos | Proporción |", "|---|---:|---:|---:|"]
    lines += [f"| {name} | {entry['records']:,} | {entry['positive_records']:,} | {entry['positive_rate']:.4%} |"
              for name, entry in analysis["splits"].items()]
    lines += ["", "Semilla 42; partición aleatoria estratificada 70/15/15. No es validación temporal.", "",
              "## Métricas", "", "| Métrica | Validación | Prueba |", "|---|---:|---:|"]
    for key in ("roc_auc", "average_precision", "brier_score", "log_loss"):
        lines.append(f"| {key} | {metrics['validation']['logistic_regression'][key]:.6f} | {metrics['test']['logistic_regression'][key]:.6f} |")
    lines += ["", "![Curvas ROC y precisión-recall](discrimination_curves.png)", "",
              "Comparación constante: probabilidad fija igual a la tasa positiva de entrenamiento.", "",
              "| Métrica constante | Validación | Prueba |", "|---|---:|---:|"]
    for key in ("roc_auc", "average_precision", "brier_score", "log_loss"):
        lines.append(f"| {key} | {metrics['validation']['constant_baseline'][key]:.6f} | {metrics['test']['constant_baseline'][key]:.6f} |")
    lines += ["", "### Diagnóstico por umbral", "",
              "Estos umbrales son diagnósticos de clasificación, no decisiones de aprobación/rechazo ni niveles integrados en C#.", "",
              "| Partición | Umbral | Precision | Recall | F1 | Matriz [[TN, FP], [FN, TP]] |",
              "|---|---:|---:|---:|---:|---|"]
    for name in ("validation", "test"):
        for threshold, entry in metrics[name]["logistic_regression"]["classification_diagnostics"].items():
            lines.append(f"| {name} | {threshold} | {entry['precision']:.6f} | {entry['recall']:.6f} | {entry['f1']:.6f} | {entry['confusion_matrix_labels_0_1']} |")
    lines += ["", "## Calibración", "",
              "| Diagnóstico | Validación | Prueba |", "|---|---:|---:|"]
    for key in ("observed_positive_rate", "mean_predicted_probability", "ece_quantile_bins",
                "diagnostic_calibration_intercept", "diagnostic_calibration_slope"):
        lines.append(f"| {key} | {calibration['validation'][key]:.6f} | {calibration['test'][key]:.6f} |")
    uniform = pd.read_csv(reports / "calibration_test_uniform.csv")
    last = uniform.iloc[-1]
    lines += ["", "El promedio está próximo a la prevalencia y el diagnóstico global de pendiente se aproxima a 1.",
              "**Esto no demuestra calibración uniforme:** los intervalos medios subestiman eventos y la cola alta los sobreestima.",
              f"En prueba, el intervalo {last['bin']} tiene {int(last['count'])} casos: probabilidad media {last['mean_predicted_probability']:.2%} frente a {last['observed_positive_rate']:.2%} observado "
              f"(IC Wilson 95%: {last['observed_wilson_lower_95']:.2%}–{last['observed_wilson_upper_95']:.2%}).",
              "Los deciles agrupan gran parte de la cola alta en un solo intervalo y pueden ocultar ese problema.",
              "ECE depende de los intervalos y no garantiza calibración local. Brier y log loss también reflejan discriminación.",
              "Intercepto y pendiente son ajustes diagnósticos sobre cada holdout, no un calibrador aplicado al modelo.",
              "", "![Calibración por deciles](calibration.png)", "",
              "![Calibración por intervalos uniformes](calibration_uniform.png)", "",
              "## Limitaciones", "", *[f"- {item}" for item in metadata["limitations"]],
              "- DebtRatio conserva valores extremos incluso tras filtrar ingresos positivos (máximo 61.106,5); log1p atenúa su escala, pero no certifica la calidad del dato.",
              "- Edad observada elegible: 21–103 años. No demuestra desempeño en solicitantes de 18–20 años ni garantiza población laboral activa.",
              "- No se cambió el baseline tras observar las métricas de prueba; futuras mejoras requieren una nueva evaluación independiente.",
              "", "## Artefactos", "",
              "- `../artifacts/model.joblib`: pipeline completo; recarga verificada con probabilidades idénticas.",
              "- `../artifacts/metadata.json`: versión, objetivo, variables, configuración, hashes y dependencias.",
              "- `dataset_analysis.json`, `excluded_rows.csv` y `split_assignments.csv`: trazabilidad de preparación y particiones.",
              "- `metrics.json`, `calibration_summary.json` y tablas de calibración: resultados completos.",
              "- `validation_predictions.csv` y `test_predictions.csv`: probabilidades para reproducción.",
              "- `coefficients.csv`: coeficientes sobre entradas transformadas/escaladas; no explicaciones causales.",
              "", "No hay API, integración C#, OCR, almacenamiento de solicitudes ni interpretación final con IA.",
              ""]
    (reports / "RESULTS.md").write_text("\n".join(lines), encoding="utf-8")
    print(f"Informe generado: {reports / 'RESULTS.md'}")


if __name__ == "__main__":
    main()

