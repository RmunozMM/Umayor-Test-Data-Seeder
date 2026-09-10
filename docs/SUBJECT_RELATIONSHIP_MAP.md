# Mapa de relaciones del sujeto (RUT/pasaporte → grafo de registros)

Fuente de verdad: `Ley_de_Proteccion_OPTIMIZADA_FAST_v4_pasaporte.sql` (query real usada hoy para
solicitudes de acceso bajo la Ley 21719, provista por el usuario el 2026-09-10). Este documento
traduce cada bloque `UNION ALL` de ese SQL a una regla declarativa (`RecordFilter` de
`DataverseMasterDataMigrator.Core`). Es la especificación que implementa
`SubjectRelationshipMap.cs` — cualquier cambio a las reglas debe actualizar este archivo primero.

## Resolución del contexto raíz

Antes de aplicar cualquier regla de tabla, se resuelve `contact` por `wit_rut` o `wit_pasaporte`
(uno de los dos, igual que el SQL) y se leen estos campos de ese único registro — `SubjectContext`:

**Formato de `contact.wit_rut`** (confirmado mirando el formulario real de Dataverse, no
asumido): guarda SOLO el cuerpo del RUT, sin el dígito verificador — el DV vive aparte en
`contact.wit_dv`. `SubjectResolver.NormalizeRut` descarta cualquier guión-y-DV que el usuario
tipee en la UI antes de buscar (p. ej. "17175272-8" o "17.175.272-8" → busca "17175272"); un
valor sin guión se usa tal cual, asumiendo que ya es el cuerpo. Primer intento de este mapeo
asumía (mal, sin evidencia real) que `wit_rut` guardaba cuerpo+DV concatenados — corregido tras
que el usuario reportara una búsqueda real que no encontraba el contacto esperado.

| Campo de `contact` | Uso |
|---|---|
| `contactid` | El "root" — casi todas las reglas filtran por este GUID. |
| `originatingleadid` | Resuelve `lead.leadid = originatingleadid`. |
| `wit_caso` | Resuelve `incident.incidentid = wit_caso`. |
| `wit_eventoorigen` | Resuelve `wit_evento.activityid = wit_eventoorigen`. |
| `wit_colegio` | Resuelve `wit_colegio.wit_colegioid = wit_colegio`. |
| `wit_tramo`, `wit_ingresobrutofamiliar` | Resuelven `wit_ingresofamiliarbruto.wit_ingresofamiliarbrutoid`. |

Todos estos campos pueden ser `null` — cuando lo son, esa condición puntual del OR simplemente no
se agrega (nunca se agrega una condición `= null`).

## Reglas por tabla (orden = el mismo del SQL)

Cada regla es un `RecordFilter` con `LogicalOperator = Or` salvo que se indique lo contrario.
"2 saltos" significa que la regla necesita una consulta previa (a otra tabla) para resolver una
lista de IDs, y luego filtra por `FilterOperator.In` sobre esa lista — no depende de que otra
regla ya se haya ejecutado antes, cada una resuelve sus propios IDs intermedios de forma
independiente.

| # | Tabla | Condiciones (OR) | Saltos |
|---|---|---|---|
| 1 | `contact` | `contactid = ContactId`, `wit_rut = Rut` | directo (el propio root — el SQL también matchea por `wit_rut`, para incluir contactos duplicados con el mismo RUT, no solo el elegido como raíz por `SubjectResolver`) |
| 2 | `lead` | `customerid = ContactId`, `parentcontactid = ContactId`, `leadid = OriginatingLeadId` | directo |
| 3 | `incident` | `customerid = ContactId`, `primarycontactid = ContactId`, `responsiblecontactid = ContactId`, `msa_partnercontactid = ContactId`, `incidentid = WitCaso` | directo |
| 4 | `phonecall` | `regardingobjectid = ContactId`, `wit_rut = Rut` | directo |
| 5 | `email` | `regardingobjectid = ContactId` | directo |
| 6 | `wit_actividadchat` | `regardingobjectid = ContactId`, `wit_rut = Rut` | directo |
| 7 | `wit_visitaweb` | `regardingobjectid = ContactId` | directo |
| 8 | `wit_visitapresencial` | `regardingobjectid = ContactId` | directo |
| 9 | `wit_evento` | `regardingobjectid = ContactId`, `activityid = WitEventoOrigen` | directo |
| 10 | `wit_procesodepostulacion` | `wit_contacto = ContactId`, `wit_referente = ContactId` | directo |
| 11 | `wit_solicituddeadmisiondirecta` | `wit_postulante = ContactId`, `wit_rut = Rut` | directo |
| 12 | `wit_historicocontactos` | `wit_contact = ContactId`, `wit_contactorelacionado = ContactId` | directo |
| 13 | `wit_historicodatosdecontactabilidad` | `wit_contacto = ContactId`, `wit_rut = Rut` | directo |
| 14 | `wit_contactodelsegmentocomercial` | `wit_contacto = ContactId`, `wit_rut = Rut` | directo |
| 15 | `wit_colegio` | `wit_colegioid = WitColegio`, `wit_coordinador = ContactId`, `wit_director = ContactId`, `wit_encargado = ContactId`, `wit_orientador = ContactId` | directo |
| 16 | `wit_ingresofamiliarbruto` | `wit_ingresofamiliarbrutoid = WitTramo`, `wit_ingresofamiliarbrutoid = WitIngresoBrutoFamiliar` | directo |
| 17 | `msdyn_ocliveworkitem` | `msdyn_customer = ContactId`, `regardingobjectid = ContactId`, `msdyn_socialprofileid IN (socialprofile.socialprofileid WHERE customerid = ContactId)` | mixto (2 condiciones directas + 1 de 2 saltos) |
| 18 | `activitymimeattachment` | `objectid IN (email.activityid WHERE regardingobjectid = ContactId)` | 2 saltos |
| 19 | `socialprofile` | `customerid = ContactId` | directo |
| 20 | `wit_historicodatosofertaacademica` | `wit_contactoid = ContactId` | directo |
| 21 | `wit_historicocarreramatriculada` | `wit_contacto = ContactId` | directo |
| 22 | `wit_contactodelacuenta` | `wit_contacto = ContactId` | directo |
| 23 | `wit_whatsapp` | `regardingobjectid = ContactId` | directo |
| 24 | `wit_sms` | `regardingobjectid = ContactId` | directo |
| 25 | `activityparty` | `partyid = ContactId` | directo |
| 26 | `activitypointer` | `activityid IN (activityparty.activityid WHERE partyid = ContactId)` | 2 saltos |
| 27 | `annotation` | `objectid = ContactId`, `objectid IN (incident.incidentid WHERE <misma regla que la fila 3>)` | mixto (1 directa + 1 de 2 saltos) — fusiona los dos `UNION ALL` del SQL (`annotation_contact` y `annotation_incident`), son la misma tabla |
| 28 | `customeraddress` | `parentid = ContactId` | directo |

**No incluidas en V1** (aparecen en el SQL pero se excluyen deliberadamente del mapa de
extracción, no de la query de conteo original):
- `attachment` (el contenido binario del archivo adjunto — depende de `activitymimeattachment`,
  fila 18; se evalúa en una fase posterior si hace falta migrar el binario, no solo su metadata).
- `annotation` vía `wit_wit_detalledeactividad_contact_resultadoultimocorreoelectronico` u otras
  relaciones N:N de `contact` no cubiertas por el SQL original — fuera de alcance porque el SQL
  de referencia tampoco las cubre.

## Salvaguarda: filtro vacío nunca significa "todos los registros" acá

`RecordFilter` (`DataverseMasterDataMigrator.Core`) define que un filtro sin condiciones
significa "sin filtro, todos los registros" — correcto para el migrador genérico, pero
catastrófico acá: varias reglas (16, 18, 26 en la primera versión) pueden terminar sin ninguna
condición resuelta cuando el contexto del sujeto no tiene los GUIDs intermedios que esa regla
necesita (p. ej. un contacto sin `wit_tramo` ni `wit_ingresobrutofamiliar`). Sin salvaguarda, eso
traería la tabla COMPLETA — datos de cualquier otro sujeto — al entorno bajo.
`SubjectProfileBuilder.BuildAsync` sustituye cualquier filtro vacío por uno que nunca matchea
ningún registro real (`<tabla>id = Guid.Empty`), en un único punto que aplica a las 28 reglas por
igual — no confiar en que cada regla nueva se acuerde de este caso por su cuenta.

## Cuidado con la primary key de las tablas Activity-type

`activitypointer`, `email`, `phonecall`, `wit_actividadchat`, `wit_visitaweb`,
`wit_visitapresencial`, `wit_evento`, `wit_whatsapp`, `wit_sms` y `msdyn_ocliveworkitem` son
todas de tipo Activity en Dataverse — su primary key real es SIEMPRE `activityid`, nunca
`<logicalname>id`. Bug real encontrado en vivo: el guard de filtro vacío de
`SubjectProfileBuilder` armaba `activitypointerid` para la fila 26, y Dataverse lo rechazó
("'ActivityPointer' entity doesn't contain attribute with Name = 'activitypointerid'"). Corregido
con una lista explícita (`SubjectProfileBuilder.ActivityTypeTables`). `activityparty` NO es
Activity-type — su propia primary key es `activitypartyid`, la convención estándar aplica ahí
sin problema (distinto del atributo `activityparty.activityid`, que es un LOOKUP, no su propia
primary key — ver fila 26 más arriba).

## Regla de fidelidad

Este mapa debe seguir siendo un superconjunto o igual al SQL de referencia — nunca un
subconjunto. Si se agrega una tabla nueva al SQL de Ley 21719 en el futuro, se agrega acá
primero, antes de tocar código.
