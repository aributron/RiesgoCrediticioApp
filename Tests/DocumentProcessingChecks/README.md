# Comprobaciones del procesamiento documental

Desde la raíz del proyecto:

```powershell
dotnet run --project Tests/DocumentProcessingChecks/DocumentProcessingChecks.csproj
```

La suite es una aplicación de consola sin paquetes de pruebas adicionales. Genera PDF sintéticos temporales con PdfPig y utiliza respuestas HTTP controladas: no consume créditos ni envía documentos a OpenAI.

Comprueba lectura, documentos sin texto, archivos inválidos o ausentes, contrato JSON, campos nulos, errores HTTP, conexión, timeout, reprocesamiento, estados, separación de los datos declarados y conservación del resultado simulado.

La antigüedad se solicita al modelo según las reglas del prompt; estas pruebas no certifican la exactitud de un modelo real.

## Prueba manual de extracción real

Copiar `appsettings.Example.json` a `appsettings.json`, completar `AiConfig:OpenAIApiKey` y mantener un modelo con soporte de JSON Schema estricto. Alternativamente, definir `OPENAI_API_KEY` en el entorno del proceso (tiene prioridad sobre el archivo).

El timeout se configura con `AiConfig:DocumentExtractionTimeoutSeconds` (1–300 segundos; predeterminado 60). La aplicación inicia sin clave; solo la extracción requiere configuración.

Ejecutar `dotnet run --project RiesgoFiscalApp.csproj`, adjuntar un recibo PDF con texto y pulsar **Procesar recibo**. Verificar los cinco campos contra el documento original. Los valores nulos deben verse como **No obtenido**, el resultado como **Pendiente de revisión**, y el ingreso declarado debe permanecer intacto.

Repetir con un PDF sin texto y con un PDF inválido. No deben producir una llamada al modelo. Los datos extraídos son de solo lectura y la simulación crediticia sigue siendo independiente.

No se implementa OCR, comparación con datos declarados, revisión aprobada, persistencia, ML ni interpretación crediticia.

