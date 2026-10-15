# Entrenamiento de riesgo crediticio

Proyecto Python independiente para el baseline académico **Give Me Some Credit**. Estima morosidad de 90 días o más a 24 meses. Incluye entrenamiento y una API local de inferencia; la integración con .NET queda pendiente.

La API se inicia desde esta carpeta con:

```powershell
.\.venv\Scripts\python.exe -m uvicorn api.main:app --host 127.0.0.1 --port 8000 --workers 1 --no-access-log
```

Ver [api/README.md](api/README.md) para dependencias, contrato, ejemplos y errores. El entorno local ya tiene instaladas las dependencias de la API.

## Entorno

Python 3.12. El entorno local preparado está en `.venv/`; el intérprete portátil y la herramienta de instalación están en `.runtime/`. Estos directorios y la caché quedan excluidos del control de versiones.

Para reproducir desde un checkout nuevo con Python 3.12 instalado:

```powershell
cd ml_service
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.lock.txt
```

`requirements.txt` fija las dependencias directas; `requirements.lock.txt` registra todas las versiones del entorno que generó el artefacto.

## Reproducción

Desde `ml_service/`:

```powershell
.\.venv\Scripts\python.exe -m training.download_dataset
.\.venv\Scripts\python.exe -m training.train
.\.venv\Scripts\python.exe -m training.report
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
```

La descarga requiere red; entrenamiento, informe y pruebas trabajan localmente. El entrenamiento escribe resultados en `artifacts/` y `reports/`; repetirlo reemplaza esos resultados. `training.report` usa resultados ya guardados y no entrena ni modifica el modelo.

Configuración: `training/config.json`. La semilla es 42, la división es 70/15/15 y C se selecciona entre 0.1, 1 y 10 por log loss de validación. El conjunto de prueba no interviene en la selección. El modelo guardado se ajusta únicamente sobre entrenamiento.

## Estructura

- `credit_risk/data.py`: carga, selección de variables y exclusiones trazables.
- `credit_risk/model.py`: Pipeline y obtención de probabilidad de la clase 1.
- `credit_risk/evaluation.py`: métricas y diagnóstico de calibración.
- `training/download_dataset.py`: descarga OpenML versionada y checksum.
- `training/train.py`: entrenamiento y artefactos reproducibles.
- `training/report.py`: informe legible y gráfico por intervalos uniformes.
- `tests/test_training.py`: pruebas de preparación, separación, Pipeline, serialización y resultados.
- `api/`: esquemas Pydantic, carga del artefacto y endpoints FastAPI.
- `tests/test_api.py`: pruebas del contrato y consistencia con el pipeline, incluyendo HTTP local con Uvicorn.
- `data/raw/`: archivo descargado localmente y metadata de origen.
- `artifacts/`: pipeline joblib y metadata del modelo.
- `reports/`: métricas, calibración, gráficos, predicciones y particiones.

## Contrato del modelo entrenado

El pipeline recibe un DataFrame con estos nombres, también registrados en metadata:

1. `age`: edad en años.
2. `DebtRatio`: compromisos mensuales —pagos de deuda, obligaciones alimentarias y gastos de vida— divididos por ingreso bruto mensual; fracción, no porcentaje.
3. `NumberOfTime30-59DaysPastDueNotWorse`: conteo de atrasos de 30–59 días sin empeorar durante los últimos 24 meses.
4. `NumberOfTime60-89DaysPastDueNotWorse`: conteo de atrasos de 60–89 días sin empeorar durante los últimos 24 meses.

`MonthlyIncome` se utiliza únicamente para seleccionar una población con ingreso conocido y positivo. El ingreso nominal no entra al modelo. Monto solicitado, plazo y antigüedad no intervienen.

La salida positiva corresponde a `SeriousDlqin2yrs=1`. La clase se busca explícitamente en `classes_` antes de seleccionar la columna de `predict_proba`.

## Resultados y límites

Ver [reports/RESULTS.md](reports/RESULTS.md) para cifras, exclusiones, calibración y comparación con el predictor constante.

Las métricas se obtienen sobre datos históricos externos y una población filtrada; no validan probabilidades para Argentina ni exclusivamente para empleados en relación de dependencia. La calibración presenta errores locales especialmente en la cola alta, aunque su promedio se acerque a la prevalencia.

Los campos de historial de mora se usarán como declarados únicamente en la futura implementación académica. En un entorno real deberían provenir de fuentes externas de información crediticia. Un dato desconocido no equivale a cero atrasos.

Las bandas Bajo/Medio/Alto se implementarán posteriormente como reglas configurables de la solución; no son decisiones automáticas. La aplicación C# conserva su comportamiento actual hasta autorizar la integración.

El artefacto joblib fue generado localmente; debe cargarse con las versiones registradas y desde una fuente confiable. Los coeficientes no son explicaciones causales.

## Fuentes

- [Competencia original](https://www.kaggle.com/competitions/GiveMeSomeCredit).
- [Metadata y diccionario de la copia OpenML 45577, versión 1](https://www.openml.org/api/v1/json/data/45577).
- [Calibración de probabilidades, scikit-learn](https://scikit-learn.org/stable/modules/calibration.html).
- [Persistencia de modelos, scikit-learn](https://scikit-learn.org/stable/model_persistence.html).

