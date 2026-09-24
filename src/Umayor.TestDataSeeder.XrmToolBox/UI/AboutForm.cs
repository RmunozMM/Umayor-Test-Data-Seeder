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
        // Isologo horizontal oficial a color de la Universidad Mayor, recortado a su contenido y
        // reducido a 48 px de alto (altura mínima en pantalla del Manual de Marca UMayor 2024) por
        // tools/make_icons.py. Nombre = RootNamespace + ruta del EmbeddedResource.
        internal const string LogoResourceName = "Umayor.TestDataSeeder.XrmToolBox.Resources.um-horizontal-color-48.png";

        // Geometría del logo (ClientSize 560x560, medida con el layout real). Área de resguardo del
        // manual = 1/4 del ancho del logo (278/4 ≈ 70 px) libre alrededor: la fila "Repositorio:"
        // termina en y=312 (logo en y=390, 78 px), el botón "Cerrar" empieza en y=515 (77 px bajo
        // el logo, que termina en y=438; en horizontal solo lo separan 31 px, por eso la distancia
        // vertical) y los bordes quedan a 141 px (laterales) y 122 px (inferior).
        private const int LogoTop = 390;

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
            ClientSize = new Size(560, 560);

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

            // Versión color del isologo solo sobre fondo blanco: va sobre el cuerpo, a tamaño real
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
