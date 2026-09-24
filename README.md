# Umayor Test Data Seeder

Plugin para **XrmToolBox** que extrae el grafo completo de registros de una persona (por **RUT** o
**pasaporte**) desde Producción, lo **anonimiza** y lo migra a un entorno bajo (DEV/QA) de
Microsoft Dataverse / Dynamics 365 — datos de prueba reales sin exponer información personal.

Reutiliza el motor de escritura de
[Dataverse Master Data Migrator](https://github.com/RmunozMM/Dataverse-Master-Data-Migrator)
(`DataverseMasterDataMigrator.Core`) sin forkearlo.

- **Autor:** Rogelio Muñoz — [www.rogeliomunoz.cl](http://www.rogeliomunoz.cl)
- **Versión actual:** 0.1.25.0
- **Plataforma:** .NET Framework 4.8, WinForms
- **Todos los derechos reservados.** El código está publicado para consulta y para descargar el
  instalador; no se autoriza su reutilización ni redistribución sin permiso del autor.

## Descargar el instalador

La versión compilada y lista para usar está en la pestaña **[Releases](../../releases)** de
este repositorio. Descargue el `.zip` de la última versión, extráigalo y ejecute
`Instalar-Plugin.bat` **con XrmToolBox cerrado**.

---

## Qué hace

Se conectan dos ambientes: **SOURCE (Producción)**, de donde se lee, y **TARGET (Entorno bajo)**,
donde se escribe. Luego se ingresa un RUT (con o sin puntos y guión: `17175272-8`,
`17.175.272-8` o `17175272`) o un pasaporte.

| Botón | Qué hace |
|---|---|
| **Resolver y Previsualizar** | Busca el `contact` raíz en Source y resuelve, tabla por tabla, todos los registros relacionados con esa persona (28 tablas — ver `docs/SUBJECT_RELATIONSHIP_MAP.md`). Muestra qué se crearía/actualizaría en Target y el **RUT anonimizado** con el que quedará. No escribe nada. |
| **Migrar (anonimizado)** | Anonimiza y escribe el grafo en Target usando el motor multipass de `DataverseMasterDataMigrator.Core`. Provisiona los `systemuser` referenciados que no existan en Target, cierra/cancela casos (`incident`) según su estado real en Source y omite los adjuntos. Al terminar informa creados/actualizados/fallidos y los mensajes de error agrupados. |
| **Diagnosticar Automatización en Target** | Solo lectura: lista los plugins (`sdkmessageprocessingstep`) y reglas de negocio registrados en Target sobre `Create` de las tablas de actividades, para explicar fallas causadas por automatización del ambiente. |
| **Eliminar del Target** | Borra de Target (nunca de Source) los registros del sujeto resuelto, en orden inverso de creación, para limpiar un sujeto de prueba antes de volver a migrarlo. Pide confirmación explícita (por defecto "No"). |
| **Limpiar Log** | Vacía el log de la pantalla entre corridas. |
| **About** | Muestra versión, autor y enlaces del proyecto. La versión instalada también se ve junto al botón, arriba a la derecha. |

**La anonimización no es opcional** — no existe forma de desactivarla desde la UI. Es
determinística (el mismo RUT real siempre produce el mismo RUT falso, con dígito verificador
válido), así que el sujeto se puede volver a migrar o encontrar en Target usando el RUT
anonimizado que muestra el plugin. Cubre RUT/DV, nombres, datos de contacto y fechas.

---

## Cómo compilar

Requiere **Visual Studio 2019 o superior** con soporte de .NET Framework 4.8, **o** solo el
**.NET SDK** (sin Visual Studio).

Este repositorio referencia `DataverseMasterDataMigrator.Core` por `ProjectReference` a un clon
**hermano** del repositorio del migrador, que debe llamarse exactamente
`DataverseMasterDataMigrator`:

```
<carpeta>/
    DataverseMasterDataMigrator/     <- clon de Dataverse-Master-Data-Migrator
    Umayor-Test-Data-Seeder/         <- este repositorio
```

```
git clone https://github.com/RmunozMM/Dataverse-Master-Data-Migrator.git DataverseMasterDataMigrator
git clone https://github.com/RmunozMM/Umayor-Test-Data-Seeder.git
cd Umayor-Test-Data-Seeder
dotnet build UmayorTestDataSeeder.sln -c Release
dotnet test tests\Umayor.TestDataSeeder.Tests\Umayor.TestDataSeeder.Tests.csproj -c Release
```

Los proyectos referencian `Microsoft.NETFramework.ReferenceAssemblies` vía NuGet, así que
compilan contra `net48` real sin tener instalado el Developer Pack de .NET Framework ni Visual
Studio.

### Sobre las referencias en `lib/`

Las DLL de terceros necesarias para compilar (XrmToolBox, SDK de Dataverse, McTools,
Newtonsoft.Json) están versionadas en **`lib/`**, así que un clon nuevo compila sin pasos
previos. Deben coincidir en versión con el XrmToolBox real donde se va a instalar el plugin — un
tamaño de archivo igual no garantiza la misma `AssemblyName.Version` (ver `lib/README.txt`).

---

## Cómo empaquetar el instalador

Un instalador es un `.zip` con:

```
Instalar-Plugin.bat                        <- raíz del repo
Install-Umayor-Test-Data-Seeder.ps1        <- raíz del repo
bin/
    Umayor.TestDataSeeder.dll              <- compilado desde bin\Release (XrmToolBox)
    Umayor.TestDataSeeder.Core.dll         <- mismo bin\Release
    DataverseMasterDataMigrator.Core.dll   <- mismo bin\Release (copiado por el ProjectReference)
```

El script instala `Umayor.TestDataSeeder.dll` en la raíz de
`%APPDATA%\MscrmTools\XrmToolBox\Plugins\` y las dos dependencias propias en la subcarpeta
`Plugins\Umayor.TestDataSeeder\` — **nunca** en la raíz, para no chocar con el
`DataverseMasterDataMigrator.Core.dll` que instala el migrador genérico en su propia subcarpeta.
No se copian las DLL del host (SDK de Dataverse, XrmToolBox, McTools, Newtonsoft.Json) ni
ClosedXML: este plugin no usa el exportador a Excel de Core.

XrmToolBox debe estar cerrado (si está abierto mantiene el DLL bloqueado; el script lo detecta y
pide confirmación). El `.zip` en sí **no** está versionado (`dist/` está en `.gitignore`) — se
publica como *GitHub Release*.

---

## Estructura

```
UmayorTestDataSeeder.sln
Instalar-Plugin.bat, Install-Umayor-Test-Data-Seeder.ps1
/src
    /Umayor.TestDataSeeder.Core          <- resolución del sujeto, perfil, anonimización, limpieza en Target
        /SubjectGraph                    <- SubjectResolver, SubjectRelationshipMap, SubjectProfileBuilder, SubjectTargetCleaner
        /Anonymization                   <- RutGenerator, PiiFaker, SubjectAnonymizingTransformer
    /Umayor.TestDataSeeder.XrmToolBox    <- plugin: Plugin.cs, PluginControl, adaptadores SDK
/tests
    /Umayor.TestDataSeeder.Tests         <- xUnit, sin conexión Dataverse real
/docs
    SUBJECT_RELATIONSHIP_MAP.md          <- especificación de las reglas por tabla (fuente de verdad)
/lib
    README.txt, Newtonsoft.Json.dll, Microsoft.Xrm.Sdk.dll, XrmToolBox.Extensibility.dll, ...
```

---

## Trampas ya pagadas (leer antes de tocar la resolución o la migración)

Defectos reales encontrados probando contra el tenant real. El detalle de cada uno está en el
historial de commits.

**1. `contact.wit_rut` guarda solo el cuerpo del RUT, sin DV.**
El dígito verificador vive aparte en `contact.wit_dv`. `SubjectResolver.NormalizeRut` descarta
lo que venga después del guión antes de buscar. Un primer intento asumió cuerpo+DV concatenado
sin evidencia y ninguna búsqueda real encontraba el contacto.

**2. Previsualizar sin filtro lee tablas completas de Producción.**
Si los filtros del sujeto no llegan a `MigrationPreviewBuilder.BuildAsync`, se pagina la tabla
entera (una corrida real tardó más de 70 minutos antes de cancelarla). Todo camino que lea de
Source debe recibir el filtro del sujeto.

**3. La clave primaria no siempre es `<tabla>id`.**
Las tablas de tipo actividad usan `activityid` (`activitypointer` no tiene
`activitypointerid`). Además, los adaptadores de `Services/` son una **copia** de los del
migrador genérico, no se comparten: un fix aplicado allá no llega aquí solo — hay que buscar el
mismo patrón en ambos repos.

**4. Atributos válidos para escribir pero no para leer (`contact.subscriptionid`).**
El adaptador de metadata debe poblar `IsValidForRead`; si no, el `Retrieve` falla. Mismo motivo
que el punto 3: el fix del migrador no se heredó automáticamente.

**5. Un lookup obligatorio sin destino en Target tumba al sujeto completo en cascada.**
Si `contact` falla, todo lo que lo referencia falla. Por eso este perfil usa
`RequiredLookupPolicy = OptionalLookupPolicy = SkipSilently` (a diferencia del migrador
genérico). `activitypointer` y `activityparty` se resuelven pero no se escriben: Dataverse no
permite crearlos directamente.

**6. `incident` no se puede cerrar con un `Update` genérico.**
Requiere `CloseIncidentRequest` / cancelación explícita, que el plugin aplica después de
escribir según el `statecode` real de Source.

**7. "Entity 'SystemUser' Does Not Exist" en `phonecall`/`email`/`wit_visitaweb`.**
Ni forzar `ownerid` ni `BypassCustomPluginExecution` (vía Upsert o Create/Update explícito)
lo resolvieron. La solución fue provisionar en Target los `systemuser` referenciados desde
Source que falten (mismo id, nombre/correo), usando la unidad de negocio del usuario conectado
en Target — Dataverse rechaza el `Create` sin `businessunitid` y la unidad de Source no existe
en Target.

**8. Adjuntos (`activitymimeattachment`).**
El tenant exige `ObjectTypeCode`+`ObjectId`, nunca también `activityId`. Finalmente se excluyen
siempre, por espacio de almacenamiento; nada del grafo depende de sus ids.

**9. XrmToolBox cachea metadata de cada plugin por `AssemblyQualifiedName`.**
Toda versión que se vaya a instalar de verdad sube `AssemblyVersion`/`AssemblyFileVersion` en
`Properties/AssemblyInfo.cs`, nunca solo el DLL.

---

## Diagnóstico

El plugin escribe su log en el panel de la propia pantalla (botón **Limpiar Log** para
vaciarlo); no genera archivo de log propio, así que copie el contenido antes de cerrar si lo
necesita.

Cada migración deja un `ExecutionManifest` con checkpoints en
`%APPDATA%\MscrmTools\XrmToolBox\Umayor.TestDataSeeder\Executions\`.

Tras una migración con fallas "Entity ... Does Not Exist", el plugin vuelve a leer el registro
de Source y registra qué atributo contenía la referencia rota.

Para que `BypassCustomPluginExecution` tenga efecto, el usuario conectado a Target necesita el
privilegio **"Bypass custom business logic"** en su rol de seguridad; si no lo tiene, Dataverse
ignora el flag sin avisar.

---

## Mejoras pendientes

- Compartir los adaptadores de `Services/` con el migrador genérico en vez de mantener una copia
  (evita repetir fixes, ver trampas 3 y 4).
- `customeraddress.freighttermscode`: diferencia real de valores de OptionSet entre Source y
  Target — requiere homologar la personalización, no es un bug de código.
- Íconos reales de `Plugin.cs`.
- Las DLL de `lib/` podrían venir de paquetes NuGet en lugar de estar versionadas.
