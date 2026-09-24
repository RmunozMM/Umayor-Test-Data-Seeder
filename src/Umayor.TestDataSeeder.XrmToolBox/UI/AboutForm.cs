using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Umayor.TestDataSeeder.XrmToolBox.UI
{
    /// <summary>
    /// Mismo estilo que el About de DataverseMasterDataMigrator (a su vez calcado del de Metadata
    /// Dataverse Document): header oscuro con título/versión, cuerpo blanco con descripción +
    /// enlaces de desarrollador/contacto/repositorio.
    /// </summary>
    internal sealed class AboutForm : Form
    {
        // Logo de la aplicación (escudo + "Umayor / TEST DATA SEEDER") provisto por el autor,
        // recortado a su contenido y reducido a 64 px de alto (218x64) por tools/make_icons.py.
        // Nombre = RootNamespace + ruta del EmbeddedResource.
        internal const string LogoResourceName = "Umayor.TestDataSeeder.XrmToolBox.Resources.app-logo-64.png";

        // Geometría del logo (ClientSize 560x500, medida con el layout real): la fila
        // "Repositorio:" termina en y=312 (logo en y=352, 40 px), el logo termina en y=416 y el
        // botón "Cerrar" empieza en y=455 (39 px; en horizontal además los separan 61 px) y los
        // bordes quedan a 171 px (laterales) y 84 px (inferior).
        private const int LogoTop = 352;

        private Image _logo;

        public AboutForm()
        {
            Text = "About Umayor Test Data Seeder";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 500);

            var version = Assembly.GetExecutingAssembly().GetName().Version;

            // Gris 2 de la paleta UMayor.
            var header = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = ColorTranslator.FromHtml("#343742") };
            header.Controls.Add(new Label
            {
                Text = "Umayor Test Data Seeder",
                ForeColor = Color.White,
                Font = new Font(FontFamily.GenericSansSerif, 15, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 18)
            });
            var versionRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Location = new Point(20, 55),
                BackColor = Color.Transparent
            };
            versionRow.Controls.Add(new Label
            {
                Text = $"Version {version}",
                ForeColor = Color.Silver,
                Font = new Font(FontFamily.GenericSansSerif, 9),
                AutoSize = true,
                Margin = new Padding(0)
            });
            versionRow.Controls.Add(new Label
            {
                Text = " | ",
                ForeColor = Color.Silver,
                Font = new Font(FontFamily.GenericSansSerif, 9),
                AutoSize = true,
                Margin = new Padding(0)
            });
            var repoLink = new LinkLabel
            {
                Text = "Enlace al repositorio",
                LinkColor = Color.Silver,
                ActiveLinkColor = Color.White,
                VisitedLinkColor = Color.Silver,
                Font = new Font(FontFamily.GenericSansSerif, 9),
                AutoSize = true,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            repoLink.LinkClicked += (s, e) =>
            {
                try { Process.Start("https://github.com/RmunozMM/Umayor-Test-Data-Seeder"); }
                catch { /* no default handler registered for this link type — nothing sensible to do about it here */ }
            };
            versionRow.Controls.Add(repoLink);
            header.Controls.Add(versionRow);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(20),
                BackColor = Color.White
            };
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            body.Controls.Add(new Label
            {
                Text = "Extrae el grafo de registros de un RUT o pasaporte desde Producción, lo anonimiza y lo migra " +
                       "a un entorno bajo (DEV/QA) de Microsoft Dataverse / Dynamics 365 — datos de prueba reales " +
                       "sin exponer información personal. Reutiliza el motor de Dataverse Master Data Migrator.",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 80,
                Margin = new Padding(0, 0, 0, 10)
            });

            body.Controls.Add(new Label
            {
                Text = "Desarrollador: Rogelio Muñoz",
                Font = new Font(FontFamily.GenericSansSerif, 8.25f, FontStyle.Bold),
                AutoSize = true,
                Dock = DockStyle.Top
            });
            body.Controls.Add(new Label
            {
                Text = $"Copyright © {DateTime.Now.Year} Rogelio Muñoz. Todos los derechos reservados.",
                Font = new Font(FontFamily.GenericSansSerif, 8.25f, FontStyle.Bold),
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 10)
            });

            body.Controls.Add(MakeLinkRow("Sitio Web:", "www.rogeliomunoz.cl", "https://www.rogeliomunoz.cl"));
            body.Controls.Add(MakeLinkRow("Contacto:", "rmunoz1612@gmail.com", "mailto:rmunoz1612@gmail.com"));
            body.Controls.Add(MakeLinkRow("Repositorio:", "github.com/RmunozMM/Umayor-Test-Data-Seeder", "https://github.com/RmunozMM/Umayor-Test-Data-Seeder"));

            var closeButton = new Button { Text = "Cerrar", DialogResult = DialogResult.OK, Width = 90, Height = 30 };
            closeButton.Location = new Point(ClientSize.Width - closeButton.Width - 20, ClientSize.Height - closeButton.Height - 15);
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            // WinForms acomoda los controles acoplados en orden Z inverso: el Fill tiene que quedar
            // delante del Top para que ocupe solo el espacio restante. Con el orden anterior
            // (header y luego body) el body tomaba el form completo y el header tapaba sus
            // primeros 90 px (la descripción quedaba oculta).
            Controls.Add(body);
            Controls.Add(header);
            Controls.Add(closeButton);
            closeButton.BringToFront();

            // El logo tiene fondo blanco: va sobre el cuerpo blanco, a tamaño real
            // (sin escalar ni deformar), centrado horizontalmente, con Location absoluta.
            _logo = LoadLogo();
            if (_logo != null)
            {
                var logoBox = new PictureBox
                {
                    Image = _logo,
                    SizeMode = PictureBoxSizeMode.Normal,
                    Size = _logo.Size,
                    BackColor = Color.White,
                    Location = new Point((ClientSize.Width - _logo.Width) / 2, LogoTop),
                    Anchor = AnchorStyles.Top,
                    TabStop = false
                };
                Controls.Add(logoBox);
                logoBox.BringToFront();
            }

            AcceptButton = closeButton;
            CancelButton = closeButton;
        }

        internal static Image LoadLogo()
        {
            try
            {
                using (var stream = typeof(AboutForm).Assembly.GetManifestResourceStream(LogoResourceName))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    // Copia desacoplada del stream (Image.FromStream exige mantenerlo abierto).
                    using (var decoded = Image.FromStream(stream))
                    {
                        return new Bitmap(decoded);
                    }
                }
            }
            catch
            {
                return null; // sin logo antes que romper el About
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing); // primero los controles (el PictureBox aún referencia la imagen)

            if (disposing && _logo != null)
            {
                _logo.Dispose();
                _logo = null;
            }
        }

        private static Control MakeLinkRow(string label, string linkText, string target)
        {
            var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4) };
            row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 3, 6, 0) });

            var link = new LinkLabel { Text = linkText, AutoSize = true, Margin = new Padding(0, 3, 0, 0) };
            link.LinkClicked += (s, e) =>
            {
                try { Process.Start(target); }
                catch { /* no default handler registered for this link type — nothing sensible to do about it here */ }
            };
            row.Controls.Add(link);

            return row;
        }
    }
}
