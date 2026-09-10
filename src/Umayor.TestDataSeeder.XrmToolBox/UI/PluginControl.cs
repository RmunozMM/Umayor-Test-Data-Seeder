using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using DataverseMasterDataMigrator.Core.Migration;
using DataverseMasterDataMigrator.Core.Models;
using DataverseMasterDataMigrator.Core.Planning;
using DataverseMasterDataMigrator.Core.Validation;
using Umayor.TestDataSeeder.Core.Anonymization;
using Umayor.TestDataSeeder.Core.SubjectGraph;
using Umayor.TestDataSeeder.XrmToolBox.Services;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace Umayor.TestDataSeeder.XrmToolBox.UI
{
    /// <summary>
    /// UI mínima, deliberadamente simple frente a DataverseMasterDataMigrator: un único flujo
    /// (RUT/pasaporte → Resolver y Previsualizar → Migrar), sin pestañas ni perfiles editables —
    /// el "perfil" de esta herramienta se genera en memoria por <see cref="SubjectProfileBuilder"/>
    /// cada vez, nunca se persiste ni se edita a mano.
    ///
    /// La anonimización (<see cref="SubjectAnonymizingTransformer"/>) NUNCA es opcional acá — no
    /// hay ningún checkbox para desactivarla. Decisión deliberada dado que esta herramienta existe
    /// específicamente para no copiar PII real a un entorno bajo.
    /// </summary>
    public partial class PluginControl : MultipleConnectionsPluginControlBase, IAboutPlugin
    {
        private const string TargetConnectionName = "AdditionalOrganization";

        private IOrganizationService _sourceService;
        private IOrganizationService _targetService;
        private DataverseMetadataProviderAdapter _sourceMetadata;
        private DataverseMetadataProviderAdapter _targetMetadata;
        private DataverseRecordServiceAdapter _sourceRecords;
        private DataverseRecordServiceAdapter _targetRecords;

        private System.Windows.Forms.Label _sourceLabel, _targetLabel;
        private Panel _sourceStatusDot, _targetStatusDot;
        private Button _btnChangeSource, _btnChangeTarget;

        private TextBox _rutBox, _pasaporteBox;
        private Button _btnPreview, _btnMigrate, _btnCancel;
        private Button _btnDiagnoseAutomation, _btnClearLog;
        private TextBox _logBox;

        private CancellationTokenSource _currentOperationCts;
        private SubjectProfileResult _lastResolved;
        private ExecutionManifestStore _manifestStore;

        public PluginControl()
        {
            BuildLayout();
        }

        public string HelpUrl => "https://github.com/RmunozMM/Umayor-Test-Data-Seeder";

        public void ShowAboutDialog()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            MessageBox.Show(
                $"Umayor Test Data Seeder\nVersión {version}\n\n" +
                "Extrae el grafo de registros de un RUT/pasaporte desde Producción, lo anonimiza " +
                "y lo migra a un entorno bajo — datos de prueba reales sin exponer PII.\n\n" +
                "Rogelio Muñoz — www.rogeliomunoz.cl\n" +
                "Repositorio: github.com/RmunozMM/Umayor-Test-Data-Seeder (privado)",
                "About Umayor Test Data Seeder", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            var executionsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MscrmTools", "XrmToolBox", "Umayor.TestDataSeeder", "Executions");
            _manifestStore = new ExecutionManifestStore(executionsFolder);
        }

        protected override void ConnectionDetailsUpdated(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
        }

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            bool isTarget = string.Equals(actionName, TargetConnectionName, StringComparison.OrdinalIgnoreCase);

            if (isTarget)
            {
                _targetService = newService;
                _targetMetadata = newService != null ? new DataverseMetadataProviderAdapter(newService) : null;
                _targetRecords = newService != null ? new DataverseRecordServiceAdapter(newService) : null;
                _targetLabel.Text = FormatConnectionLabel(detail);
                _targetStatusDot.BackColor = newService != null ? Color.MediumSeaGreen : Color.Silver;
            }
            else
            {
                _sourceService = newService;
                _sourceMetadata = newService != null ? new DataverseMetadataProviderAdapter(newService) : null;
                _sourceRecords = newService != null ? new DataverseRecordServiceAdapter(newService) : null;
                _sourceLabel.Text = FormatConnectionLabel(detail);
                _sourceStatusDot.BackColor = newService != null ? Color.MediumSeaGreen : Color.Silver;
            }

            base.UpdateConnection(newService, detail, actionName, parameter);
            UpdateButtonState();
        }

        private static string FormatConnectionLabel(ConnectionDetail detail)
        {
            if (detail == null) return "Not connected";
            return $"{detail.ConnectionName}\n{detail.WebApplicationUrl}\n{detail.Organization}";
        }

        private void UpdateButtonState()
        {
            bool connected = _sourceService != null && _targetService != null;
            _btnPreview.Enabled = connected;
            _btnMigrate.Enabled = connected && _lastResolved != null;
            _btnDiagnoseAutomation.Enabled = _targetService != null;
        }

        // --- Layout ---------------------------------------------------------------------------

        private void BuildLayout()
        {
            Dock = DockStyle.Fill;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(15) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            root.Controls.Add(BuildConnectionsRow(), 0, 0);
            root.Controls.Add(BuildSubjectRow(), 0, 1);

            _logBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(FontFamily.GenericMonospace, 9),
                Margin = new Padding(0, 10, 0, 0)
            };
            root.Controls.Add(_logBox, 0, 2);

            Controls.Add(root);
        }

        private Control BuildConnectionsRow()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 1, Height = 170, Padding = new Padding(0, 0, 0, 10) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));

            var sourceCard = BuildConnectionCard("SOURCE (Producción)", out _sourceLabel, out _sourceStatusDot, out _btnChangeSource, "Change Source Connection");
            _btnChangeSource.Click += (s, e) => RaiseRequestConnectionEvent(new RequestConnectionEventArgs());

            var targetCard = BuildConnectionCard("TARGET (Entorno bajo)", out _targetLabel, out _targetStatusDot, out _btnChangeTarget, "Select Target Connection");
            _btnChangeTarget.Click += (s, e) => AddAdditionalOrganization();

            var arrow = new System.Windows.Forms.Label
            {
                Text = "→",
                Font = new Font(FontFamily.GenericSansSerif, 24, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                ForeColor = Color.Silver
            };

            row.Controls.Add(sourceCard, 0, 0);
            row.Controls.Add(arrow, 1, 0);
            row.Controls.Add(targetCard, 2, 0);
            return row;
        }

        private static Panel BuildConnectionCard(string title, out System.Windows.Forms.Label detailLabel, out Panel statusDot, out Button actionButton, string buttonText)
        {
            var card = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(18), BackColor = Color.White };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            var dot = new Panel { Size = new Size(12, 12), BackColor = Color.Silver, Margin = new Padding(2, 7, 0, 0) };
            var titleLabel = new System.Windows.Forms.Label { Text = title, Font = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            var detail = new System.Windows.Forms.Label { Text = "Not connected", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Padding = new Padding(0, 8, 0, 8), ForeColor = Color.DimGray };
            var button = new Button { Text = buttonText, Dock = DockStyle.Fill, Height = 30, Margin = new Padding(0, 4, 0, 0) };

            layout.Controls.Add(dot, 0, 0);
            layout.Controls.Add(titleLabel, 1, 0);
            layout.Controls.Add(detail, 0, 1);
            layout.SetColumnSpan(detail, 2);
            layout.Controls.Add(button, 0, 2);
            layout.SetColumnSpan(button, 2);

            card.Controls.Add(layout);
            detailLabel = detail;
            statusDot = dot;
            actionButton = button;
            return card;
        }

        private Control BuildSubjectRow()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 6, RowCount = 1, AutoSize = true, Padding = new Padding(0, 8, 0, 14) };

            row.Controls.Add(new System.Windows.Forms.Label { Text = "RUT:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 8, 6, 0) });
            _rutBox = new TextBox { Width = 130, Margin = new Padding(0, 5, 20, 0) };
            row.Controls.Add(_rutBox);

            row.Controls.Add(new System.Windows.Forms.Label { Text = "Pasaporte:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 8, 6, 0) });
            _pasaporteBox = new TextBox { Width = 130, Margin = new Padding(0, 5, 20, 0) };
            row.Controls.Add(_pasaporteBox);

            _btnPreview = MakeButton("Resolver y Previsualizar", OnPreview);
            _btnPreview.Enabled = false;
            row.Controls.Add(_btnPreview);

            _btnMigrate = MakeButton("Migrar (anonimizado)", OnMigrate);
            _btnMigrate.Enabled = false;
            row.Controls.Add(_btnMigrate);

            _btnCancel = MakeButton("Cancel", OnCancel);
            _btnCancel.Enabled = false;
            row.Controls.Add(_btnCancel);

            _btnDiagnoseAutomation = MakeButton("Diagnosticar Automatización en Target", OnDiagnoseAutomation);
            _btnDiagnoseAutomation.Enabled = false;
            row.Controls.Add(_btnDiagnoseAutomation);

            _btnClearLog = MakeButton("Limpiar Log", OnClearLog);
            row.Controls.Add(_btnClearLog);

            return row;
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
            button.Click += onClick;
            return button;
        }

        // --- Acciones ---------------------------------------------------------------------------

        private void OnCancel(object sender, EventArgs e) => _currentOperationCts?.Cancel();

        private void OnClearLog(object sender, EventArgs e) => _logBox.Clear();

        private void OnPreview(object sender, EventArgs e)
        {
            var rut = _rutBox.Text.Trim();
            var pasaporte = _pasaporteBox.Text.Trim();
            if (string.IsNullOrEmpty(rut) && string.IsNullOrEmpty(pasaporte))
            {
                MessageBox.Show("Ingresa un RUT o un pasaporte.", "Falta el sujeto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _currentOperationCts = new CancellationTokenSource();
            var token = _currentOperationCts.Token;
            _btnPreview.Enabled = false;
            _btnMigrate.Enabled = false;
            _btnDiagnoseAutomation.Enabled = false;
            _btnCancel.Enabled = true;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Resolviendo sujeto y previsualizando...",
                Work = (worker, args) =>
                {
                    SetWorkingMessage("Resolviendo contacto por RUT/pasaporte...");
                    var result = SubjectProfileBuilder.BuildAsync(_sourceRecords, rut, pasaporte, token).GetAwaiter().GetResult();
                    if (result == null) { args.Result = new PreviewOutcome { Resolved = null, Preview = null }; return; }

                    token.ThrowIfCancellationRequested();
                    SetWorkingMessage("Cargando metadata de las tablas del mapa de relaciones...");
                    var sourceTables = LoadTableMetadata(result.Profile, _sourceMetadata, token);
                    var plan = MigrationPlanner.CreatePlan(result.Profile, sourceTables);

                    // CRÍTICO: sin esto, MigrationPreviewBuilder pagina la tabla COMPLETA de
                    // Source por cada tabla del mapa (todo Producción) en vez de solo lo del
                    // sujeto — el mismo Filter que ya lleva cada ProfileEntity (armado por
                    // SubjectProfileBuilder) tiene que llegar hasta acá. Bug real: la primera
                    // versión de este método no lo pasaba, y una corrida tardó 70+ minutos
                    // leyendo tablas enteras sin ningún indicio de que eso era lo que pasaba.
                    var entityFilters = result.Profile.Entities
                        .Where(pe => pe.Enabled)
                        .ToDictionary(pe => pe.LogicalName, pe => pe.Filter, StringComparer.OrdinalIgnoreCase);

                    SetWorkingMessage("Contando registros por tabla...");
                    var preview = new MigrationPreviewBuilder()
                        .BuildAsync(plan, sourceTables, _sourceRecords, _targetRecords, pageSize: 500, maxRecordsPerTable: 0, token,
                            msg => SetWorkingMessage(msg), entityFilters)
                        .GetAwaiter().GetResult();

                    args.Result = new PreviewOutcome { Resolved = result, Preview = preview };
                },
                PostWorkCallBack = args =>
                {
                    _btnPreview.Enabled = true;
                    _btnCancel.Enabled = false;
                    UpdateButtonState();

                    if (args.Error is OperationCanceledException) { AppendLog("Cancelado."); return; }
                    if (args.Error != null) { MessageBox.Show(args.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

                    var outcome = (PreviewOutcome)args.Result;
                    if (outcome.Resolved == null)
                    {
                        AppendLog($"No se encontró ningún contact con RUT '{rut}' / pasaporte '{pasaporte}'.");
                        _lastResolved = null;
                        UpdateButtonState();
                        return;
                    }

                    _lastResolved = outcome.Resolved;

                    AppendLog($"=== SUJETO RESUELTO: contactid={outcome.Resolved.Context.ContactId} ===");
                    if (!string.IsNullOrWhiteSpace(outcome.Resolved.Context.Rut))
                    {
                        var fakeRut = RutGenerator.Generate(outcome.Resolved.Context.Rut);
                        AppendLog($"RUT anonimizado con el que va a quedar en Target: {fakeRut.Formatted} (siempre el mismo para este RUT real, no cambia entre corridas).");
                    }
                    foreach (var table in outcome.Preview.OrderBy(t => t.LogicalName, StringComparer.OrdinalIgnoreCase))
                    {
                        AppendLog($"{table.LogicalName}: {table.SourceRecordCount} en Source -> {table.ToCreate} a crear, {table.ToUpdate} a actualizar.");
                    }
                    AppendLog("Previsualización lista. Todo lo anterior se anonimizará antes de escribirse en Target.");

                    UpdateButtonState();
                }
            });
        }

        private void OnMigrate(object sender, EventArgs e)
        {
            if (_lastResolved == null) return;

            var fakeRutText = !string.IsNullOrWhiteSpace(_lastResolved.Context.Rut)
                ? $"\n\nQueda con el RUT anonimizado {RutGenerator.Generate(_lastResolved.Context.Rut).Formatted} en Target."
                : string.Empty;

            if (MessageBox.Show(
                    $"Se va a migrar (anonimizado) el grafo de registros del sujeto contactid={_lastResolved.Context.ContactId} hacia Target.{fakeRutText}\n\n¿Confirmas?",
                    "Confirmar migración", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _currentOperationCts = new CancellationTokenSource();
            var token = _currentOperationCts.Token;
            _btnPreview.Enabled = false;
            _btnMigrate.Enabled = false;
            _btnDiagnoseAutomation.Enabled = false;
            _btnCancel.Enabled = true;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Migrando (anonimizado)...",
                Work = (worker, args) =>
                {
                    var profile = _lastResolved.Profile;

                    try
                    {
                        var who = (WhoAmIResponse)_targetService.Execute(new WhoAmIRequest());
                        profile.Options.OwnerIdOverride = who.UserId;
                        AppendLog($"Mitigación activa: los registros se crearán con owner = usuario conectado a Target ({who.UserId}), para evitar fallas de SystemUser huérfanos de Origen.");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"No se pudo obtener el usuario conectado a Target para aplicar el override de owner — se continúa sin ese override. ({ex.Message})");
                    }

                    SetWorkingMessage("Cargando metadata de las tablas del mapa de relaciones...");
                    var sourceTables = LoadTableMetadata(profile, _sourceMetadata, token);
                    var targetTables = LoadTableMetadata(profile, _targetMetadata, token);
                    var plan = MigrationPlanner.CreatePlan(profile, sourceTables);

                    var request = new MigrationExecutionRequest
                    {
                        Profile = profile,
                        Plan = plan,
                        SourceTables = sourceTables,
                        TargetTables = targetTables,
                        SourceRecords = _sourceRecords,
                        TargetRecords = _targetRecords,
                        SourceMetadata = _sourceMetadata,
                        Transformer = new SubjectAnonymizingTransformer(),
                        SourceLabel = _sourceLabel.Text,
                        TargetLabel = _targetLabel.Text,
                        Logger = new PluginExecutionLogger(AppendLog),
                        ManifestStore = _manifestStore
                    };

                    var manifest = new MigrationExecutor().ExecuteAsync(request, token).GetAwaiter().GetResult();
                    DiagnoseEntityNotFoundFailures(manifest, sourceTables, profile, token);
                    args.Result = manifest;
                },
                PostWorkCallBack = args =>
                {
                    _btnPreview.Enabled = true;
                    _btnCancel.Enabled = false;
                    UpdateButtonState();

                    if (args.Error is OperationCanceledException) { AppendLog("Migración cancelada."); return; }
                    if (args.Error != null) { MessageBox.Show(args.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

                    var manifest = (ExecutionManifest)args.Result;
                    var totalCreated = manifest.Tables.Sum(t => t.Created);
                    var totalUpdated = manifest.Tables.Sum(t => t.Updated);
                    var totalFailed = manifest.Tables.Sum(t => t.Failed);

                    foreach (var t in manifest.Tables)
                        LogFailureDetails(t);

                    var fakeRutLine = !string.IsNullOrWhiteSpace(_lastResolved.Context.Rut)
                        ? $" RUT anonimizado en Target: {RutGenerator.Generate(_lastResolved.Context.Rut).Formatted}."
                        : string.Empty;
                    AppendLog($"=== MIGRACIÓN COMPLETA (anonimizada): {totalCreated} creados, {totalUpdated} actualizados, {totalFailed} fallidos.{fakeRutLine} ===");
                    MessageBox.Show($"Listo. {totalCreated} creados, {totalUpdated} actualizados, {totalFailed} fallidos.{fakeRutLine}",
                        "Migración completa", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            });
        }

        /// <summary>
        /// Diagnóstico de solo lectura contra Target: busca automatización (plugin steps y
        /// workflows/reglas de negocio) registrada sobre el mensaje Create de phonecall/email/
        /// wit_visitaweb. Existe porque en producción real la migración de esas 3 tablas falla con
        /// "Entity 'SystemUser' With Id = ... Does Not Exist" sin que el GUID venga de nuestro
        /// payload (se descartó forzando <c>ownerid</c> explícitamente) — la hipótesis que queda es
        /// que algo registrado en Target sobre Create referencia un SystemUser inexistente en ese
        /// entorno. El usuario no tiene el Plugin Trace Log habilitado ni maneja el Plugin
        /// Registration Tool, así que esto reutiliza <see cref="_targetService"/> (ya autenticado)
        /// en vez de pedir credenciales o herramientas externas. No toca Source ni el estado de la
        /// migración — es puramente informativo.
        /// </summary>
        private void OnDiagnoseAutomation(object sender, EventArgs e)
        {
            _currentOperationCts = new CancellationTokenSource();
            var token = _currentOperationCts.Token;
            _btnPreview.Enabled = false;
            _btnMigrate.Enabled = false;
            _btnDiagnoseAutomation.Enabled = false;
            _btnCancel.Enabled = true;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Diagnosticando automatización registrada en Target...",
                Work = (worker, args) =>
                {
                    AppendLog("=== DIAGNÓSTICO DE AUTOMATIZACIÓN EN TARGET (solo lectura, mensaje Create) ===");

                    var tablesToCheck = new[] { "phonecall", "email", "wit_visitaweb" };
                    foreach (var table in tablesToCheck)
                    {
                        token.ThrowIfCancellationRequested();
                        SetWorkingMessage($"Diagnosticando '{table}'...");
                        AppendLog($"--- Tabla: {table} ---");
                        DiagnosePluginStepsOnCreate(table);
                        DiagnoseActiveWorkflowsOnCreate(table);
                    }

                    AppendLog("=== FIN DEL DIAGNÓSTICO DE AUTOMATIZACIÓN ===");
                },
                PostWorkCallBack = args =>
                {
                    _btnCancel.Enabled = false;
                    UpdateButtonState();

                    if (args.Error is OperationCanceledException) { AppendLog("Diagnóstico cancelado."); return; }
                    if (args.Error != null) { MessageBox.Show(args.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                }
            });
        }

        /// <summary>
        /// Plugin steps (sdkmessageprocessingstep) habilitados y registrados sobre el mensaje
        /// Create, filtrados por tabla vía sdkmessagefilter.primaryobjecttypecode. Un segundo nivel
        /// de join (plugintype -> pluginassembly, anidado con <see cref="LinkEntity.AddLink"/> sobre
        /// el LinkEntity de plugintype, no sobre el QueryExpression) trae el nombre del tipo y del
        /// ensamblado que lo contiene — firmas de AddLink/LinkEntity/AliasedValue confirmadas contra
        /// el Microsoft.Xrm.Sdk.dll real de este repo antes de escribir esto.
        /// </summary>
        private void DiagnosePluginStepsOnCreate(string tableLogicalName)
        {
            try
            {
                var query = new QueryExpression("sdkmessageprocessingstep")
                {
                    ColumnSet = new ColumnSet("name", "stage", "mode")
                };
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

                var messageLink = query.AddLink("sdkmessage", "sdkmessageid", "sdkmessageid", JoinOperator.Inner);
                messageLink.LinkCriteria.AddCondition("name", ConditionOperator.Equal, "Create");

                var filterLink = query.AddLink("sdkmessagefilter", "sdkmessagefilterid", "sdkmessagefilterid", JoinOperator.Inner);
                filterLink.LinkCriteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, tableLogicalName);

                var pluginTypeLink = query.AddLink("plugintype", "plugintypeid", "plugintypeid", JoinOperator.Inner);
                pluginTypeLink.EntityAlias = "plugintype";
                pluginTypeLink.Columns = new ColumnSet("typename", "friendlyname");

                var assemblyLink = pluginTypeLink.AddLink("pluginassembly", "pluginassemblyid", "pluginassemblyid", JoinOperator.Inner);
                assemblyLink.EntityAlias = "assembly";
                assemblyLink.Columns = new ColumnSet("name");

                var result = _targetService.RetrieveMultiple(query);

                if (result.Entities.Count == 0)
                {
                    AppendLog($"    plugin steps en Create: ningún plugin step registrado en Create para '{tableLogicalName}'.");
                    return;
                }

                foreach (var record in result.Entities)
                {
                    var stepName = record.GetAttributeValue<string>("name") ?? "(sin nombre)";
                    var stage = record.GetAttributeValue<OptionSetValue>("stage")?.Value;
                    var mode = record.GetAttributeValue<OptionSetValue>("mode")?.Value;
                    var typeName = record.GetAttributeValue<AliasedValue>("plugintype.typename")?.Value as string;
                    var friendlyName = record.GetAttributeValue<AliasedValue>("plugintype.friendlyname")?.Value as string;
                    var assemblyName = record.GetAttributeValue<AliasedValue>("assembly.name")?.Value as string;

                    AppendLog($"    plugin step '{stepName}': tipo='{friendlyName ?? typeName ?? "?"}' ({typeName ?? "?"}), ensamblado='{assemblyName ?? "?"}', stage={DescribePluginStage(stage)}, mode={DescribePluginMode(mode)}.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"    no se pudo consultar plugin steps en Create para '{tableLogicalName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Workflows/reglas de negocio activos (statecode=1 Activated) que disparan en Create
        /// (triggeroncreate=true) sobre la tabla dada.
        /// </summary>
        private void DiagnoseActiveWorkflowsOnCreate(string primaryEntity)
        {
            try
            {
                var query = new QueryExpression("workflow")
                {
                    ColumnSet = new ColumnSet("name", "category")
                };
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 1); // Activated
                query.Criteria.AddCondition("triggeroncreate", ConditionOperator.Equal, true);
                query.Criteria.AddCondition("primaryentity", ConditionOperator.Equal, primaryEntity);

                var result = _targetService.RetrieveMultiple(query);

                if (result.Entities.Count == 0)
                {
                    AppendLog($"    workflows/reglas de negocio activos en Create: ningún workflow activo para '{primaryEntity}'.");
                    return;
                }

                foreach (var record in result.Entities)
                {
                    var name = record.GetAttributeValue<string>("name") ?? "(sin nombre)";
                    var category = record.GetAttributeValue<OptionSetValue>("category")?.Value;
                    AppendLog($"    workflow/regla activo en Create '{name}': categoría={DescribeWorkflowCategory(category)}.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"    no se pudo consultar workflows activos en Create para '{primaryEntity}': {ex.Message}");
            }
        }

        private static string DescribePluginStage(int? stage)
        {
            switch (stage)
            {
                case 10: return "Pre-validation";
                case 20: return "Pre-operation";
                case 40: return "Post-operation";
                default: return stage?.ToString() ?? "?";
            }
        }

        private static string DescribePluginMode(int? mode)
        {
            switch (mode)
            {
                case 0: return "Sync";
                case 1: return "Async";
                default: return mode?.ToString() ?? "?";
            }
        }

        private static string DescribeWorkflowCategory(int? category)
        {
            switch (category)
            {
                case 0: return "Workflow";
                case 1: return "Dialog";
                case 2: return "Regla de negocio (Business Rule)";
                case 3: return "Action";
                case 4: return "Business Process Flow";
                case 5: return "Modern Flow (Power Automate)";
                default: return category?.ToString() ?? "?";
            }
        }

        /// <summary>
        /// Diagnóstico puntual: Dataverse reporta "Entity 'X' With Id = &lt;guid&gt; Does Not
        /// Exist" pero NUNCA dice qué atributo del registro que se intentó escribir contenía esa
        /// referencia — sin esto, un fallo así (p. ej. contra "SystemUser") es indiagnosticable a
        /// simple vista. Re-lee el registro real de Source (mismo id que falló) y busca cuál de
        /// sus atributos apunta exactamente a ese GUID, para nombrarlo en el log.
        /// </summary>
        private void DiagnoseEntityNotFoundFailures(
            ExecutionManifest manifest, Dictionary<string, TableSummary> sourceTables, MigrationProfile profile, CancellationToken token)
        {
            var pattern = new System.Text.RegularExpressions.Regex(
                @"Entity '([^']+)' With Id = ([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}) Does Not Exist");

            foreach (var table in manifest.Tables)
            {
                if (!sourceTables.TryGetValue(table.LogicalName, out var sourceTable)) continue;
                var entityConfig = profile.Entities.FirstOrDefault(e => string.Equals(e.LogicalName, table.LogicalName, StringComparison.OrdinalIgnoreCase));
                if (entityConfig == null) continue;

                var restoreState = profile.Options.RestoreStateStatus && sourceTable.HasStateStatus;
                var writableAttrs = AttributeWritabilityRules.GetWritableAttributes(sourceTable, entityConfig, restoreState);
                var columns = writableAttrs.Select(a => a.LogicalName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                var examplesPerMessage = table.Errors
                    .Where(e => e.Outcome == RecordOutcome.Failed)
                    .GroupBy(e => e.ErrorMessage ?? string.Empty)
                    .Select(g => g.First());

                foreach (var failure in examplesPerMessage)
                {
                    var match = pattern.Match(failure.ErrorMessage ?? string.Empty);
                    if (!match.Success) continue;

                    token.ThrowIfCancellationRequested();
                    var targetGuid = Guid.Parse(match.Groups[2].Value);

                    IReadOnlyList<DataRecord> records;
                    try { records = _sourceRecords.RetrieveByIdsAsync(table.LogicalName, new[] { failure.RecordId }, columns, token).GetAwaiter().GetResult(); }
                    catch (Exception ex)
                    {
                        AppendLog($"    ↳ diagnóstico: no se pudo releer '{table.LogicalName}' (registro {failure.RecordId:D}) para identificar el campo — {ex.Message}");
                        continue;
                    }

                    var record = records.FirstOrDefault();
                    if (record == null)
                    {
                        AppendLog($"    ↳ diagnóstico: '{table.LogicalName}' (registro {failure.RecordId:D}) ya no está en Source, no se pudo releer para identificar el campo.");
                        continue;
                    }

                    var matchingAttrs = record.Attributes
                        .Where(kvp => kvp.Value is DataReference dr && dr.Id == targetGuid)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    if (matchingAttrs.Count == 0)
                    {
                        AppendLog($"    ↳ diagnóstico: en '{table.LogicalName}' (registro {failure.RecordId:D}), ningún atributo LEÍDO apunta a {targetGuid:D} — puede ser un campo excluido de antemano (p. ej. un lookup de tipo Owner) que igual viaja en el payload real, u otra causa. Atributos con valor DataReference en el registro: {string.Join(", ", record.Attributes.Where(kvp => kvp.Value is DataReference).Select(kvp => kvp.Key))}.");
                    }

                    if (matchingAttrs.Count > 0)
                    {
                        AppendLog($"    ↳ diagnóstico: en '{table.LogicalName}' (registro {failure.RecordId:D}), el/los campo(s) '{string.Join(", ", matchingAttrs)}' apunta(n) a '{match.Groups[1].Value}' {targetGuid:D}, inexistente en Target.");
                    }
                }
            }
        }

        private Dictionary<string, TableSummary> LoadTableMetadata(
            MigrationProfile profile, DataverseMetadataProviderAdapter metadata, CancellationToken token)
        {
            var tables = new Dictionary<string, TableSummary>(StringComparer.OrdinalIgnoreCase);
            var entities = profile.Entities.Where(x => x.Enabled).ToList();
            for (int i = 0; i < entities.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var entity = entities[i];
                SetWorkingMessage($"Cargando metadata: {entity.LogicalName} ({i + 1}/{entities.Count})...");
                try { tables[entity.LogicalName] = metadata.GetTableDetailAsync(entity.LogicalName, CancellationToken.None).GetAwaiter().GetResult(); }
                catch { /* tabla ausente en este ambiente — el motor ya maneja esto de forma defensiva */ }
            }

            PatchActivityMimeAttachmentDependency(tables);
            return tables;
        }

        /// <summary>
        /// Bug real encontrado en vivo: <c>activitymimeattachment</c> corría ANTES que
        /// <c>email</c> (el correo todavía no existía en Target), porque la metadata real de
        /// Dataverse declara el lookup <c>objectid</c> apuntando al tipo abstracto POLIMÓRFICO
        /// <c>activitypointer</c> — no a "email" concretamente. <see cref="DependencyGraphBuilder"/>
        /// (Core, correcto en general) solo arma una arista de dependencia si el LookupTarget
        /// declarado está entre las tablas del perfil; como <c>activitypointer</c> quedó excluido
        /// de la escritura (no se puede crear directamente en Dataverse — ver
        /// docs/SUBJECT_RELATIONSHIP_MAP.md), nunca se generaba ninguna arista hacia "email", y
        /// <c>activitymimeattachment</c> quedaba sin ninguna restricción de orden.
        /// docs/SUBJECT_RELATIONSHIP_MAP.md (fila 18) ya documenta que, en el alcance de ESTE
        /// mapa, <c>objectid</c> siempre apunta a un <c>email</c> — conocimiento de dominio que la
        /// metadata genérica de Dataverse no puede expresar. Se agrega "email" al LookupTargets
        /// ya declarado (no se reemplaza) para que el grafo de dependencias compartido lo capte
        /// sin tocar su lógica genérica.
        /// </summary>
        private static void PatchActivityMimeAttachmentDependency(Dictionary<string, TableSummary> tables)
        {
            if (!tables.TryGetValue("activitymimeattachment", out var table)) return;
            var objectIdAttribute = table.Attributes.FirstOrDefault(a => string.Equals(a.LogicalName, "objectid", StringComparison.OrdinalIgnoreCase));
            if (objectIdAttribute == null) return;
            if (objectIdAttribute.LookupTargets.Any(t => string.Equals(t, "email", StringComparison.OrdinalIgnoreCase))) return;

            objectIdAttribute.LookupTargets = objectIdAttribute.LookupTargets.Concat(new[] { "email" }).ToList();
        }

        private void AppendLog(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (_logBox.InvokeRequired)
                _logBox.Invoke(new Action(() => _logBox.AppendText(line + Environment.NewLine)));
            else
                _logBox.AppendText(line + Environment.NewLine);
        }

        /// <summary>
        /// Surfaces WHY records failed, not solo cuántos — gap real: el log de Migrar solo
        /// mostraba "[Pass 1] X: done — 0 created, N failed", sin ningún indicio de la causa,
        /// para 337 fallos reales de una corrida en vivo. Mismo patrón ya probado en
        /// DataverseMasterDataMigrator.XrmToolBox\UI\PluginControl.cs — agrupa por mensaje de
        /// error distinto, ya que un fallo masivo suele compartir una única causa raíz.
        /// </summary>
        private void LogFailureDetails(TableExecutionResult t)
        {
            if (t.Failed == 0) return;

            var grouped = t.Errors
                .Where(e => e.Outcome == RecordOutcome.Failed)
                .GroupBy(e => e.ErrorMessage ?? "(sin mensaje de error)")
                .OrderByDescending(g => g.Count());

            foreach (var g in grouped)
                AppendLog($"    {g.Count()}x: {g.Key}  (p.ej. registro {g.First().RecordId:D}, pass {g.First().Pass})");
        }

        /// <summary>Resultado de <see cref="OnPreview"/> pasado vía <c>WorkAsyncInfo.Args.Result</c>
        /// (que es <c>object</c>) — evita el riesgo de castear un <c>Tuple</c> nulo, que lanzaría
        /// <see cref="NullReferenceException"/> al desestructurarlo. <see cref="Resolved"/> nulo
        /// significa "no se encontró el sujeto", no un error.</summary>
        private sealed class PreviewOutcome
        {
            public SubjectProfileResult Resolved { get; set; }
            public IReadOnlyList<TableDataPreview> Preview { get; set; }
        }
    }
}
