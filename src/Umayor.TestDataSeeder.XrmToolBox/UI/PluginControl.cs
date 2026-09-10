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
            var outer = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, AutoSize = true, Padding = new Padding(0, 4, 0, 14) };

            var inputRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 6, RowCount = 1, AutoSize = true, Padding = new Padding(0, 8, 0, 4) };

            inputRow.Controls.Add(new System.Windows.Forms.Label { Text = "RUT:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 8, 6, 0) });
            _rutBox = new TextBox { Width = 130, Margin = new Padding(0, 5, 20, 0) };
            inputRow.Controls.Add(_rutBox);

            inputRow.Controls.Add(new System.Windows.Forms.Label { Text = "Pasaporte:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 8, 6, 0) });
            _pasaporteBox = new TextBox { Width = 130, Margin = new Padding(0, 5, 20, 0) };
            inputRow.Controls.Add(_pasaporteBox);

            _btnPreview = MakeButton("Resolver y Previsualizar", OnPreview);
            _btnPreview.Enabled = false;
            inputRow.Controls.Add(_btnPreview);

            _btnMigrate = MakeButton("Migrar (anonimizado)", OnMigrate);
            _btnMigrate.Enabled = false;
            inputRow.Controls.Add(_btnMigrate);

            _btnCancel = MakeButton("Cancel", OnCancel);
            _btnCancel.Enabled = false;
            inputRow.Controls.Add(_btnCancel);

            var hint = new System.Windows.Forms.Label
            {
                Text = "RUT: con o sin puntos/guión, con o sin dígito verificador (se acepta cualquiera de los dos formatos).",
                AutoSize = true,
                Dock = DockStyle.Top,
                ForeColor = Color.DimGray,
                Font = new Font(FontFamily.GenericSansSerif, 8, FontStyle.Italic),
                Margin = new Padding(0, 0, 0, 0)
            };

            outer.Controls.Add(inputRow, 0, 0);
            outer.Controls.Add(hint, 0, 1);
            return outer;
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
            button.Click += onClick;
            return button;
        }

        // --- Acciones ---------------------------------------------------------------------------

        private void OnCancel(object sender, EventArgs e) => _currentOperationCts?.Cancel();

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

                    SetWorkingMessage("Contando registros por tabla...");
                    var preview = new MigrationPreviewBuilder()
                        .BuildAsync(plan, sourceTables, _sourceRecords, _targetRecords, pageSize: 500, maxRecordsPerTable: 0, token,
                            msg => SetWorkingMessage(msg))
                        .GetAwaiter().GetResult();

                    args.Result = new PreviewOutcome { Resolved = result, Preview = preview };
                },
                PostWorkCallBack = args =>
                {
                    _btnPreview.Enabled = true;
                    _btnCancel.Enabled = false;

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

            if (MessageBox.Show(
                    $"Se va a migrar (anonimizado) el grafo de registros del sujeto contactid={_lastResolved.Context.ContactId} hacia Target. ¿Confirmas?",
                    "Confirmar migración", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _currentOperationCts = new CancellationTokenSource();
            var token = _currentOperationCts.Token;
            _btnPreview.Enabled = false;
            _btnMigrate.Enabled = false;
            _btnCancel.Enabled = true;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Migrando (anonimizado)...",
                Work = (worker, args) =>
                {
                    var profile = _lastResolved.Profile;
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

                    args.Result = new MigrationExecutor().ExecuteAsync(request, token).GetAwaiter().GetResult();
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
                    AppendLog($"=== MIGRACIÓN COMPLETA (anonimizada): {totalCreated} creados, {totalUpdated} actualizados, {totalFailed} fallidos ===");
                    MessageBox.Show($"Listo. {totalCreated} creados, {totalUpdated} actualizados, {totalFailed} fallidos.",
                        "Migración completa", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            });
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
            return tables;
        }

        private void AppendLog(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (_logBox.InvokeRequired)
                _logBox.Invoke(new Action(() => _logBox.AppendText(line + Environment.NewLine)));
            else
                _logBox.AppendText(line + Environment.NewLine);
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
