# API local de inferencia

Desde `ml_service/`, con el entorno ya preparado:

```powershell
.\.venv\Scripts\python.exe -m uvicorn api.main:app --host 127.0.0.1 --port 8000 --workers 1 --no-access-log
```

La API escucha en loopback, usa un proceso y no habilita recarga automática. Se detiene con Ctrl+C. Documentación interactiva: http://127.0.0.1:8000/docs.

En un entorno nuevo creado con `python -m venv .venv`, instalar `requirements-api.lock.txt` con pip antes de iniciar. `requirements-api.txt` define dependencias directas e incluye el lock original del baseline; `requirements-api.lock.txt` fija todas las versiones del entorno de inferencia. Los archivos originales de dependencias de entrenamiento se conservaron.

## Salud

```powershell
Invoke-RestMethod -Uri 'http://127.0.0.1:8000/health'
```

HTTP 200:

```json
{
  "status": "ok",
  "modelAvailable": true,
  "modelVersion": "gmsc-logreg-v1"
}
```

Si no se puede cargar el modelo o su metadata, el servicio inicia en estado no disponible y responde HTTP 503 con `modelAvailable: false` y un mensaje controlado.

## Predicción

`POST http://127.0.0.1:8000/v1/predict`, Content-Type: application/json:

```json
{
  "ageYears": 35,
  "debtRatio": 0.35,
  "latePayments30To59DaysLast24Months": 1,
  "latePayments60To89DaysLast24Months": 0
}
```

Respuesta real del baseline aprobado para ese ejemplo:

```json
{
  "defaultProbability": 0.1164822345899684,
  "modelVersion": "gmsc-logreg-v1",
  "predictionHorizonMonths": 24,
  "targetEvent": "delinquency_90_days_or_more"
}
```

Ejemplo ejecutable en PowerShell:

```powershell
$body = @{
    ageYears = 35
    debtRatio = 0.35
    latePayments30To59DaysLast24Months = 1
    latePayments60To89DaysLast24Months = 0
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8000/v1/predict' -ContentType 'application/json' -Body $body
```

## Validación y mapeo

Los cuatro campos son obligatorios. Se rechazan campos adicionales, null, booleanos, strings numéricos y valores no finitos.

| Entrada JSON | Variable del pipeline | Restricción |
|---|---|---|
| ageYears | age | Entero, 18–120 años |
| debtRatio | DebtRatio | Número finito no negativo; fracción, no porcentaje |
| latePayments30To59DaysLast24Months | NumberOfTime30-59DaysPastDueNotWorse | Entero entre 0 y 24 |
| latePayments60To89DaysLast24Months | NumberOfTime60-89DaysPastDueNotWorse | Entero entre 0 y 24 |

Los conteos representan cantidades reales en la ventana de 24 meses. Un campo desconocido no debe enviarse como cero.

`debtRatio` puede superar 1 y representa compromisos mensuales —deudas, obligaciones alimentarias y gastos de vida— / ingreso bruto mensual. La API recibe el ratio ya calculado; no recibe ingreso nominal, identidad ni documentos.

## Ciclo de vida del modelo

El lifespan de FastAPI carga `artifacts/model.joblib` una sola vez por inicio de proceso y lo comparte entre solicitudes. Las rutas se resuelven desde el código, no desde el directorio de ejecución.

Al iniciar se verifican metadata, SHA-256 del artefacto, variables, clases, horizonte, objetivo y versiones de scikit-learn, NumPy, SciPy, pandas y joblib. No hay descarga, entrenamiento, fit, recalibración ni escritura durante una predicción.

El mapeo construye un DataFrame con exactamente las cuatro columnas y el orden del entrenamiento. La probabilidad de clase 1 se obtiene con el mismo helper utilizado por el baseline y se valida como finita entre 0 y 1. La versión, horizonte y evento se toman de metadata.

Si se restauran o cambian los artefactos, es necesario reiniciar el servicio. No se devuelve nivel de riesgo ni una decisión de aprobación/rechazo.

## Errores

| HTTP | Código | Situación |
|---|---|---|
| 422 | invalid_input | JSON o campos inválidos |
| 503 | model_unavailable | Artefactos ausentes, inválidos o incompatibles |
| 500 | prediction_failed | Fallo de inferencia o probabilidad inválida |

Ejemplo de error:

```json
{
  "error": {
    "code": "invalid_input",
    "message": "Las entradas no cumplen el contrato de predicción.",
    "details": [
      {
        "field": "body.ageYears",
        "type": "greater_than_equal",
        "message": "Input should be greater than or equal to 18"
      }
    ]
  }
}
```

Los errores excluyen valores de entrada, contextos internos, cuerpos y excepciones de bajo nivel. La aplicación no registra solicitudes ni datos personales. El comando recomendado desactiva access logs de Uvicorn.

## Pruebas

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
```

Se verifican salud, contrato, entradas inválidas, JSON incorrecto, modelos ausentes o incompatibles, carga única, ausencia de fit, rango de probabilidades, omisión de datos sensibles y consistencia exacta con la inferencia directa.

La prueba HTTP inicia un Uvicorn temporal en loopback con puerto libre y lo detiene al terminar. Las pruebas no reentrenan ni modifican los artefactos aprobados.

## Límites

Servicio local para la PoC, sin autenticación ni despliegue externo. Mantiene las limitaciones estadísticas del baseline, incluida la calibración deficiente de probabilidades altas; exponerlo por HTTP no modifica esas propiedades.

La validación de rangos no garantiza que el caso pertenezca a la población del entrenamiento. No hay almacenamiento de solicitudes ni interpretación con IA. La aplicación C# consume esta API y calcula el nivel de riesgo localmente; ver [integración C#](../../Tests/CreditRiskChecks/README.md).

