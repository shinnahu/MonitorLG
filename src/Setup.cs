// MonitorLGSetup.exe: instala MonitorLG para el usuario actual, sin admin.
//
// MonitorLGSetup.exe                     ventana de instalación
// MonitorLGSetup.exe /S [/sin-inicio]    instala sin ventana
// Desinstalar.exe [/S]                   desinstala (también desde Configuración > Aplicaciones)
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Instalador de Monitor LG")]
[assembly: AssemblyProduct("Monitor LG")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// Todo lo que el setup escribe pasa por acá, así se puede probar con otras rutas y otro registro.
class Destinos
{
    public string Carpeta, CarpetaMenu;
    public RegistryKey Registro;

    public string App { get { return Path.Combine(Carpeta, "MonitorLG.exe"); } }
    public string Desinstalador { get { return Path.Combine(Carpeta, "Desinstalar.exe"); } }
    public string Acceso { get { return Path.Combine(CarpetaMenu, "Monitor LG.lnk"); } }

    public static Destinos DelUsuario()
    {
        return new Destinos
        {
            Carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\MonitorLG"),
            CarpetaMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Registro = Registry.CurrentUser
        };
    }
}

static class Instalacion
{
    public const string Nombre = "MonitorLG", Version = "1.0.0";
    public const string ClaveInicio = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ClaveDesinstalar = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MonitorLG";

    // Primera instalación: inicia con Windows. Al actualizar: se respeta lo que había.
    public static bool InicioPorDefecto(Destinos d)
    {
        if (!File.Exists(d.App)) return true;
        using (RegistryKey k = d.Registro.OpenSubKey(ClaveInicio))
            return k != null && k.GetValue(Nombre) != null;
    }

    public static void Instalar(Destinos d, bool conInicio)
    {
        Directory.CreateDirectory(d.Carpeta);
        Directory.CreateDirectory(d.CarpetaMenu);

        using (Stream origen = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorLG.exe"))
        {
            if (origen == null) throw new InvalidOperationException("El setup no trae MonitorLG.exe.");
            using (FileStream destino = File.Create(d.App)) origen.CopyTo(destino);
        }
        string propio = Application.ExecutablePath;
        if (!MismaRuta(propio, d.Desinstalador)) File.Copy(propio, d.Desinstalador, true);

        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic acceso = shell.CreateShortcut(d.Acceso);
        acceso.TargetPath = d.App;
        acceso.WorkingDirectory = d.Carpeta;
        acceso.Description = "Apagar y prender el monitor LG";
        acceso.Save();

        using (RegistryKey k = d.Registro.CreateSubKey(ClaveInicio))
        {
            if (conInicio) k.SetValue(Nombre, "\"" + d.App + "\"");
            else k.DeleteValue(Nombre, false);
        }
        using (RegistryKey k = d.Registro.CreateSubKey(ClaveDesinstalar))
        {
            k.SetValue("DisplayName", "Monitor LG");
            k.SetValue("DisplayVersion", Version);
            k.SetValue("DisplayIcon", d.App);
            k.SetValue("InstallLocation", d.Carpeta);
            k.SetValue("UninstallString", "\"" + d.Desinstalador + "\" /desinstalar");
            k.SetValue("QuietUninstallString", "\"" + d.Desinstalador + "\" /desinstalar /S");
            k.SetValue("EstimatedSize", (int)((new FileInfo(d.App).Length + new FileInfo(d.Desinstalador).Length) / 1024), RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
    }

    // Borra solo lo que el setup creó: si hay otros archivos en la carpeta, quedan.
    public static void Desinstalar(Destinos d)
    {
        if (File.Exists(d.App)) File.Delete(d.App);
        if (File.Exists(d.Desinstalador))
        {
            // Un .exe en uso no se puede borrar, pero sí mover.
            if (MismaRuta(Application.ExecutablePath, d.Desinstalador))
                File.Move(d.Desinstalador, Path.Combine(Path.GetTempPath(), "MonitorLG-desinstalar-" + Guid.NewGuid().ToString("N") + ".exe"));
            else
                File.Delete(d.Desinstalador);
        }
        if (File.Exists(d.Acceso)) File.Delete(d.Acceso);

        using (RegistryKey k = d.Registro.OpenSubKey(ClaveInicio, true))
            if (k != null) k.DeleteValue(Nombre, false);
        d.Registro.DeleteSubKey(ClaveDesinstalar, false);

        Environment.CurrentDirectory = Path.GetTempPath(); // una carpeta en uso no se puede borrar
        if (Directory.Exists(d.Carpeta) && !Directory.EnumerateFileSystemEntries(d.Carpeta).Any())
        {
            try { Directory.Delete(d.Carpeta); }
            catch (IOException) { } // abierta en el Explorador: queda vacía
        }
    }

    static bool MismaRuta(string a, string b)
    {
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }
}

// El MonitorLG que está corriendo y lo que el setup necesita saber de la PC.
static class Sistema
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_Interface_List_SizeW(out uint largo, ref Guid guid, string id, uint flags);

    public static bool HayParsec()
    {
        var guid = new Guid("00b41627-04c4-429e-a26e-0265cf50c8fa");
        uint largo;
        return CM_Get_Device_Interface_List_SizeW(out largo, ref guid, null, 0) == 0 && largo > 1;
    }

    public static bool LGEnEscritorio()
    {
        for (uint i = 0; ; i++)
        {
            var placa = new DisplayDevice();
            placa.Size = Marshal.SizeOf(placa);
            if (!EnumDisplayDevices(null, i, ref placa, 0)) return false;
            if ((placa.Flags & 1) == 0) continue;
            for (uint j = 0; ; j++)
            {
                var monitor = new DisplayDevice();
                monitor.Size = Marshal.SizeOf(monitor);
                if (!EnumDisplayDevices(placa.Name, j, ref monitor, 1)) break;
                if ((monitor.Flags & 1) != 0 && monitor.Id.IndexOf("#GSM", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
        }
    }

    public static Process[] EnEjecucion()
    {
        int sesion = Process.GetCurrentProcess().SessionId;
        return Process.GetProcessesByName("MonitorLG").Where(p => p.SessionId == sesion).ToArray();
    }

    public static void Detener(Process[] procesos)
    {
        if (procesos.Length == 0) return;
        EventWaitHandle salir;
        if (EventWaitHandle.TryOpenExisting(@"Local\MonitorLG.salir", out salir))
            using (salir) salir.Set();
        foreach (Process p in procesos)
        {
            if (p.WaitForExit(20000)) continue;
            p.Kill(); // seguro: sin sus pings el driver retira la pantalla virtual y vuelve el LG
            p.WaitForExit(5000);
        }
    }

    public static void Iniciar(string exe, bool apagado)
    {
        Process.Start(new ProcessStartInfo(exe)
        {
            Arguments = apagado ? "apagar" : "",
            WorkingDirectory = Path.GetDirectoryName(exe),
            UseShellExecute = true
        });
    }
}

class VentanaSetup : Form
{
    readonly CheckBox inicio;
    public bool ConInicio { get { return inicio.Checked; } }

    public VentanaSetup(Destinos d, bool hayParsec)
    {
        bool actualiza = File.Exists(d.App);
        Text = "Instalar Monitor LG";
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var contenido = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(18) };
        contenido.Controls.Add(Texto("Monitor LG", new Font("Segoe UI Semibold", 16f), SystemColors.ControlText));
        contenido.Controls.Add(Texto("Apaga y prende el LG desde la bandeja o con Ctrl+Alt+F10. El mouse y el teclado no lo despiertan.", Font, SystemColors.ControlText));
        contenido.Controls.Add(Texto("Se instala solo para tu usuario, sin admin, en:\n" + d.Carpeta, Font, SystemColors.GrayText));
        if (!hayParsec)
            contenido.Controls.Add(Texto("Falta el driver Parsec VDD. Se puede instalar, pero no va a poder apagar el monitor hasta que lo tengas.", Font, Color.Firebrick));

        inicio = new CheckBox { Text = "Iniciar con Windows", AutoSize = true, Checked = Instalacion.InicioPorDefecto(d), Margin = new Padding(3, 6, 3, 14) };
        contenido.Controls.Add(inicio);

        var botones = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right };
        var cancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        var instalar = new Button { Text = actualiza ? "Actualizar" : "Instalar", AutoSize = true, DialogResult = DialogResult.OK };
        botones.Controls.Add(cancelar);
        botones.Controls.Add(instalar);
        contenido.Controls.Add(botones);
        Controls.Add(contenido);
        AcceptButton = instalar;
        CancelButton = cancelar;
    }

    static Label Texto(string texto, Font fuente, Color color)
    {
        return new Label { Text = texto, Font = fuente, ForeColor = color, AutoSize = true, UseMnemonic = false, MaximumSize =new Size(460, 0), Margin = new Padding(3, 0, 3, 12) };
    }
}

static class ProgramaSetup
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Func<string, bool> tiene = x => args.Any(a => string.Equals(a, x, StringComparison.OrdinalIgnoreCase));
        bool silencioso = tiene("/S");
        bool desinstalar = tiene("/desinstalar") ||
            string.Equals(Path.GetFileName(Application.ExecutablePath), "Desinstalar.exe", StringComparison.OrdinalIgnoreCase);
        Destinos d = Destinos.DelUsuario();
        return desinstalar ? Desinstalar(d, silencioso) : Instalar(d, silencioso, tiene("/sin-inicio"));
    }

    static int Instalar(Destinos d, bool silencioso, bool sinInicio)
    {
        bool conInicio;
        if (silencioso)
            conInicio = !sinInicio && Instalacion.InicioPorDefecto(d);
        else
            using (var ventana = new VentanaSetup(d, Sistema.HayParsec()))
            {
                if (ventana.ShowDialog() != DialogResult.OK) return 1;
                conInicio = ventana.ConInicio;
            }

        string anterior = null;
        bool estabaApagado = false;
        try
        {
            Process[] procesos = Sistema.EnEjecucion();
            if (procesos.Length > 0)
            {
                anterior = procesos[0].MainModule.FileName;
                estabaApagado = !Sistema.LGEnEscritorio(); // al actualizar queda como estaba
                Sistema.Detener(procesos);
            }
            Instalacion.Instalar(d, conInicio);
            Sistema.Iniciar(d.App, estabaApagado);
        }
        catch (Exception e)
        {
            if (!silencioso) MessageBox.Show("No se pudo instalar:\n" + e.Message, "Monitor LG", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (anterior != null && File.Exists(anterior) && Sistema.EnEjecucion().Length == 0) Sistema.Iniciar(anterior, estabaApagado);
            return 2;
        }
        if (!silencioso) MessageBox.Show("Instalado. El ícono está en la bandeja, junto al reloj.", "Monitor LG", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    static int Desinstalar(Destinos d, bool silencioso)
    {
        if (!silencioso && MessageBox.Show("¿Desinstalar Monitor LG? Si el monitor está apagado, se va a prender.", "Monitor LG",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
        try
        {
            Sistema.Detener(Sistema.EnEjecucion());
            Instalacion.Desinstalar(d);
        }
        catch (Exception e)
        {
            if (!silencioso) MessageBox.Show("No se pudo desinstalar:\n" + e.Message, "Monitor LG", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
        if (!silencioso) MessageBox.Show("Monitor LG se desinstaló.", "Monitor LG", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }
}
