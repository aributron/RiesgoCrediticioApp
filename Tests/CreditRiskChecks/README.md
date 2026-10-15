# Integración crediticia C# ↔ FastAPI

## Ejecutar la PoC

En una terminal, desde la raíz del repositorio:

```powershell
cd ml_service
.\.venv\Scripts\python.exe -m uvicorn api.main:app --host 127.0.0.1 --port 8000 --workers 1 --no-access-log
```

En otra terminal, desde la raíz:

```powershell
dotnet run --project RiesgoFiscalApp.csproj
```

C# no inicia ni entrena Python automáticamente. La extracción documental conserva su servicio y su configuración propios.

## Configuración

Las secciones siguientes están en `appsettings.Example.json` y se agregaron al archivo local `appsettings.json`, conservando las credenciales existentes:

```json
{
  "CreditRiskApi": {
    "BaseUrl": "http://127.0.0.1:8000",
    "TimeoutSeconds": 15
  },
  "RiskLevels": {
    "LowUpperExclusive": 0.10,
    "MediumUpperExclusive": 0.25
  }
}
```

BaseUrl debe ser un origen HTTP local sin ruta, query ni credenciales. El timeout admite 1–300 segundos. `RiskLevelService` valida que 0 < límite bajo < límite medio < 1. La aplicación muestra Evaluación no disponible ante configuración inválida; no utiliza un resultado de respaldo.

## Formulario y ratio

Ingreso bruto mensual declarado y compromisos y gastos mensuales declarados corresponden al mismo período y moneda. Los compromisos comprenden pagos de deuda, obligaciones alimentarias y gastos habituales de vida.

`debtRatio = compromisosMensuales / ingresoBrutoMensualDeclarado`

El cálculo usa decimal y se convierte a número JSON sin redondeo anticipado, sin incluir la cuota hipotética del crédito y sin limitar el ratio a 1.

Edad: entero entre 18 y 120. Ambos conteos de atrasos: enteros entre 0 y 24 para los últimos 24 meses. Compromisos pueden ser cero si se declara explícitamente. Los datos desconocidos comienzan vacíos y no se convierten en cero.

Los ingresos y otros datos extraídos del recibo permanecen separados y no alimentan automáticamente la predicción. Monto solicitado, plazo y antigüedad no son predictores del baseline.

## Contrato enviado

```json
{
  "ageYears": 35,
  "debtRatio": 0.35,
  "latePayments30To59DaysLast24Months": 1,
  "latePayments60To89DaysLast24Months": 0
}
```

Se envía exclusivamente este DTO. No se envían nombre, DNI, documentos, texto extraído ni credenciales de OpenAI.

Respuesta real verificada desde C# mediante la API local:

```json
{
  "defaultProbability": 0.1164822345899684,
  "modelVersion": "gmsc-logreg-v1",
  "predictionHorizonMonths": 24,
  "targetEvent": "delinquency_90_days_or_more"
}
```

La solicitud de prueba declara edad 35, ingreso bruto 1.200.000, compromisos 420.000, atrasos 1/0, monto 1.500.000 y plazo 12 meses, y adjunta un PDF sintético.

Resultado presentado:
- Probabilidad estimada de morosidad grave a 24 meses: aproximadamente 11,65 %.
- Nivel de riesgo: Medio.
- Horizonte de evaluación: 24 meses.
- Evento evaluado: Morosidad grave de 90 días o más.
- Versión: gmsc-logreg-v1.

El identificador técnico del evento se conserva y valida internamente. El resultado es apoyo para el analista, sin aprobación o rechazo automático.

## Errores y respuestas desactualizadas

Se distinguen conexión fallida, timeout, cancelación, HTTP 422, HTTP 500/503, otros errores HTTP y JSON inválido/incompleto/incompatible. Se validan los cuatro campos de respuesta, la probabilidad finita entre 0 y 1, versión no vacía, horizonte 24 y evento esperado. No se muestran cuerpos de error completos.

Ante un fallo, el resultado anterior se limpia y se presenta **Evaluación no disponible** con un mensaje apropiado. El servicio de evaluación simulada fue eliminado.

Cada evaluación captura un DTO independiente, identificador de solicitud y revisión de datos. Editar datos invalida el resultado y solicita cancelación. Se descartan tanto respuestas exitosas como errores de una revisión anterior, aunque el servicio ignore la cancelación.

## Pruebas

Desde la raíz del repositorio:

```powershell
dotnet run --project Tests/CreditRiskChecks/CreditRiskChecks.csproj
dotnet run --project Tests/DocumentProcessingChecks/DocumentProcessingChecks.csproj
```

Con FastAPI iniciado, verificar además el recorrido real:

```powershell
dotnet run --project Tests/CreditRiskChecks/CreditRiskChecks.csproj -- --integration http://127.0.0.1:8000
```

La suite de riesgo verifica ratio, límites, desconocidos, DTO sin identidad, errores HTTP, timeout, configuración, contrato de respuesta, estados, cancelación y descarte de respuestas/errores anteriores. El modo integración verifica la probabilidad y metadata contra el baseline real aprobado.

Resultados de esta implementación: 232 comprobaciones de riesgo aisladas; 235 con integración real; 179 documentales; 33 pruebas Python. No se hicieron llamadas reales a OpenAI ni se reentrenó el modelo.

Las limitaciones de calibración y representatividad del baseline siguen documentadas en `ml_service/reports/RESULTS.md`. La interfaz se verificó mediante compilación XAML y estados del ViewModel; no se realizó revisión visual interactiva.

