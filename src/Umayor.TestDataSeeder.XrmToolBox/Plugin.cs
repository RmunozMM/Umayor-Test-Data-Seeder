using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;
using Umayor.TestDataSeeder.XrmToolBox.UI;

namespace Umayor.TestDataSeeder.XrmToolBox
{
    // Iconos: 32x32 (SmallImageBase64) y 80x80 (BigImageBase64), los mismos tamaños que usan
    // DataverseMasterDataMigrator y Metadata Dataverse Document. PNG fuente en Resources/
    // (icon_small_32.png, icon_big_80.png); se regeneran con tools/make_icons.py, que además
    // imprime el base64 y su largo. Los íconos son el escudo del logo de la aplicación provisto
    // por el autor (Resources/app-logo-source.png), con el fondo exterior transparente, los
    // blancos interiores opacos y sin el texto (ilegible a 32 px), guardados como PNG de paleta
    // de 256 colores con alfa para que el base64 quede muy por debajo del límite. Volver a
    // correr tools/verify_attr_blobs.py del repo hermano contra el DLL cada vez que cambien:
    // cualquier string de ExportMetadata sobre 16383 caracteres arriesga la
    // CustomAttributeFormatException que ya tumbó a Metadata Dataverse Document.
    [Export(typeof(IXrmToolBoxPlugin)),
        ExportMetadata("Name", "Umayor Test Data Seeder"),
        ExportMetadata("Description", "Extrae el grafo de registros de un RUT/pasaporte desde Producción, lo anonimiza y lo migra a un entorno bajo."),
        ExportMetadata("PluginType", "DataMigration"),
        ExportMetadata("SmallImageBase64", "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAMAAABEpIrGAAABy1BMVEUAAAD+/v78xBQYGRckJSgBWmakARjZ2dkCR1P/2Cf9ySj7vQK2tLN2ensFZnXu6e0NDQv//uI1hY+cAANopLDHy9b/6Gr+31ONkpFSU0//3D8yMjEAOknQ5OpoaXG2CiW9yMampqaNw9NcZGrW2u0AUF710tn97Zvlycz/+Y3ns7qIiIX32GxnXVxamq3JQVZSVGFncIlJSEgveYM6Qkb78MGqEiv//KwALD3/7X9aho3/3S9Pk5z/zxJZo6p8ayV7gpCGu8CbwcTNg5O6Ijoja3Oyho3JTWHGPVGqlpa4p3fC2uCp0NKvq5zBAAbAHzYdHBzYaHrJaJ+5Vme+Rlm6MUW8KUHWeJjCmBLbx3qGnJyQlK3//yd3tM/j+/5eYGDpnalnWSjlqbTt2u7x6NFCQj9VNzxKKjFZHCUkcXyve4gjXGI1OEMIcYsgISQAAABrZV3Z0tA3ODX//6r9z0HS0sPCubODf3/L0tFfX175zj5GODgqf/90dHIqKig3NzaOjo5ZWljFxcXU1dVUU1NHR0ctLS3X0tE7Ozo0NTMAAQdNTEzExcaLi4+ZmZmqlpaljH+xn5+7qqqysrK9zc7////Mu7t/f39jY2M8L3H9AAAAmXRSTlMA////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////EpWfAnsLsyHhTIUTFy/3sRdVC9hmjc+t+csDdfAgDxMTLjkP+wZlGUCdXRY4AAACbklEQVR42pWT91fTUBzF+b7MZjTNgDRN01JKyh4KBQqEvYsat6JS95blQFRSkbJFHKj8uQaBUhA5h++P737ePd/z3r15eSecT8eqPzIrTjrzP+bnTNqxa/0a5qTnXv/rPDfl2M87ikWIGf56l1mbzlEnMu7d+o58sTHgKZf7ywVjRMOxxYWtXf33Ml57gxIbVcSygVKSlMma24JQyOvLX3aA2bF8sSzAsohhkAs0NJBhWb5yPlaIfdwDKA9rqarFMMw2EO7jB3tIuScXaPGcqQ1VWoh1d5AvpuSHT8NkzQGH65p1jfcESkS429/96OzjvjA5sA/gVMtQ+01LAyjOpwC4lOYfIA85qKfa70R5HcNs/DQPECYvN5zLBQKoYIiClM+2MZ8OECPJS+JVDtvYBxjGigMX8Vf4IxEBYjJ5IZEoxieyO3hYxMbBxkM8/2BcgHvd8RKGVe9/yAJlBYkCETjd5/PhFQBx1UKIQZ5XO8A6RhUpt6oN4COcOykhwSK0/azWu5c7hMO1BU16GMbGdV0fD23rjMqwLeLq7m+lq0ZNyR/lI7wLhBpZlBABKgOjn/e+e2ZMkJqfeevh77CMCMlmRVSq32QDsVI1WhelOxTB1SsRGL108InSNPwrC8w63HDviGQSUicg0aBpwkvQxObX/UxlsNIi99RreuMJIIJ1rQRBK0u5mVywS5OES7woEYuapC6CIMzNbwdSu+YMQivtOkCdmTRNb7Bz8lCu5xyNMiQCgOhylwm2zU8cTv56GgtRBkB1V5CmO+enj6jO1iJWFdJA8irJpfdHt2tranFhPvl28vtxBV3dOFnf/wAszXW4FU7t+QAAAABJRU5ErkJggg=="),
        ExportMetadata("BigImageBase64", "iVBORw0KGgoAAAANSUhEUgAAAFAAAABQCAMAAAC5zwKfAAABlVBMVEUAAAD+/v7+ySUwMC8qKikBXGisEikCZ3T8xhtKS0umAxv55ZXy8vJWVleysrKampjNzc2sCyP9++YAS1ba2tqrq6vm5+cBVF8WGRr52GhzdXT67bT61FKQkJCHub3CXGv8yy9paWi+VmYwcnmbwsYkJCOrHzSuzNC719lKi5Ls5891p6xalJrqxs4se4PbiJWVvME8gosoGRwCgpLbpq/al6Pq19l+bjL888gKe4kWKCptXynNeoeBrrL60D40NDMZbngQX2lpmp+4RlmVGi1/s7c4Mh2hAhi0Ok1cXmZsoKfDw8Lz3OLJZ3exKj/534nkusNfXl1caGa9vb5gVlXoprDVk558Hi1EPSXhnKkhIR/85H2jop9cnKZDQ0Neo6pfJyw/IiVgLjVyZR8cGht9fX07QEk7OzuFh4f66J8QERS3uLfK4OHW5+nvxDLjtLoZR03/1yyAIi4AAACrq6pnZ2dXV1dnaGf4+PeJiYloa22goJ2AgYL10VhKSkr33Il6enk8PEB5eXpPUE/JycmPjor7zyuTqfo/AAAAh3RSTlMA/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////y2ew3MTVDjpjGLuU3EbzCcdu/IrEv0ZAAAIZklEQVR42u2Y+1saVxrHPcwBZ3CGOzPIRUEYxWskppZEZcBEaUKjsbbpZnSnm13YgSBu3KeARpe1mvTv3vecGVAUvCTZ/tT3F/R59MP3vZ9zBgb+tD/Ifv26tGZLE+pfi3l41NAURlEUrdH8+BXE1TkdYAxmFIYB5unhF9FOW5oCKKzqMU3FDEFy9aPPpH08bWgEwchYcIpIdApYxqBV0VuNo8P7a2toOjiJZVkrFZBhrjJHmPAtOtc4/fU+SQUaaFOwzGz5RUCF0uj1I/jkCyVNljFJka437ub8UZ0kFUxWla1dH1A8Q2F2E43ln75ehN/EaonopDnSW/WbhX48ojklcZOVLSelVYajLMsOoVG7224be/Q91VkWGFU2mFqrn1BwlNOgPEjc9Jif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"),
        ExportMetadata("BackgroundColor", "WhiteSmoke"),
        ExportMetadata("PrimaryFontColor", "Black"),
        ExportMetadata("SecondaryFontColor", "Gray")]
    public class Plugin : PluginBase
    {
        // Mismo mecanismo de aislamiento de dependencias que DataverseMasterDataMigrator.Plugin —
        // ver ese archivo para el razonamiento completo. Este tool hoy no tiene dependencias
        // propias con desajuste de versión conocido (no usa ClosedXML), así que no hace falta la
        // lista de redirección forzada — solo el resolver general por subcarpeta propia.
        private static int _resolverRegistered;

        public Plugin()
        {
            if (Interlocked.Exchange(ref _resolverRegistered, 1) == 0)
            {
                AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolveEventHandler;
            }
        }

        public override IXrmToolBoxPluginControl GetControl()
        {
            return new PluginControl();
        }

        private static Assembly AssemblyResolveEventHandler(object sender, ResolveEventArgs args)
        {
            try
            {
                var thisAssembly = typeof(Plugin).Assembly;
                var requested = new AssemblyName(args.Name);

                if (args.RequestingAssembly != null && args.RequestingAssembly != thisAssembly)
                {
                    return null;
                }

                bool isOwnDependency = thisAssembly
                    .GetReferencedAssemblies()
                    .Any(a => string.Equals(a.Name, requested.Name, StringComparison.OrdinalIgnoreCase));
                if (!isOwnDependency)
                {
                    return null;
                }

                string pluginsDir = Path.GetDirectoryName(thisAssembly.Location);
                string ownFolder = Path.GetFileNameWithoutExtension(thisAssembly.Location);
                string ownSubfolder = Path.Combine(pluginsDir, ownFolder);
                string candidate = Path.Combine(ownSubfolder, requested.Name + ".dll");

                if (!File.Exists(candidate))
                {
                    return null;
                }

                if (requested.Version != null)
                {
                    var candidateName = AssemblyName.GetAssemblyName(candidate);
                    if (candidateName.Version != null && candidateName.Version < requested.Version)
                    {
                        return null;
                    }
                }

                return Assembly.LoadFrom(candidate);
            }
            catch
            {
                return null;
            }
        }
    }
}
