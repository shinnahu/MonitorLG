// MonitorLG.exe: ícono en la bandeja para apagar y prender el LG.
//
// El LG ULTRAGEAR probado siguió informando "encendido" tras DDC/CI D6 = 04 y 05. Por eso se le corta la señal:
// el escritorio pasa a una pantalla virtual (Parsec VDD) y la salida del LG se desactiva. El mouse y el
// teclado no la reactivan. La configuración no se guarda en Windows: si el programa muere, el driver
// retira la pantalla virtual al segundo y Windows vuelve al LG.
//
// Clic izquierdo o Ctrl+Alt+F10: alternar. Clic derecho: menú.
// MonitorLG.exe apagar | encender | alternar | salir: le da la orden al que ya está abierto.
//
// Compilar: build.cmd
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Registro
{
    static readonly object candado = new object();
    static readonly string archivo = Path.Combine(Path.GetTempPath(), "MonitorLG.log");

    public static void Anotar(string texto)
    {
        lock (candado)
        {
            try { File.AppendAllText(archivo, DateTime.Now.ToString("HH:mm:ss.fff ") + texto + Environment.NewLine); }
            catch (IOException) { }
        }
    }
}

// Configuración de pantallas de Windows (CCD).
static class Pantallas
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Luid { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Origen { public Luid Adaptador; public uint Id, Modo, Estado; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Destino
    {
        public Luid Adaptador;
        public uint Id, Modo, Tecnologia, Rotacion, Escala, RefrescoN, RefrescoD, Barrido;
        public int Disponible;
        public uint Estado;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Ruta { public Origen Origen; public Destino Destino; public uint Flags; }

    // Los long cubren la unión completa para que al copiar no se pierda ningún byte.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct Modo
    {
        [FieldOffset(0)] public uint Tipo;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid Adaptador;
        [FieldOffset(16)] public long A;
        [FieldOffset(24)] public long B;
        [FieldOffset(32)] public long C;
        [FieldOffset(40)] public long D;
        [FieldOffset(48)] public long E;
        [FieldOffset(56)] public long F;
        [FieldOffset(28)] public int X; // solo en modos de origen
        [FieldOffset(32)] public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NombreDestino
    {
        public uint Tipo, Tamano;
        public Luid Adaptador;
        public uint Id, Flags, Tecnologia;
        public ushort Fabricante, Producto;
        public uint Conector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Nombre;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Dispositivo;
    }

    public class Estado
    {
        public Ruta[] Rutas;
        public Modo[] Modos;
        public string[] Dispositivos; // vacío: no hay monitor detrás de esa ruta

        public bool HayLG { get { return Dispositivos.Any(EsLG); } }
        public bool HayVirtual { get { return Dispositivos.Any(EsVirtual); } }
        public bool HayOtra { get { return Dispositivos.Any(d => d != "" && !EsLG(d)); } }
        public string Resumen { get { return string.Join(",", Dispositivos.Select(d => d == "" ? "(nada)" : d.Split('#').ElementAtOrDefault(1))); } }
    }

    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint rutas, out uint modos);
    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(uint flags, ref uint nRutas, [In, Out] Ruta[] rutas, ref uint nModos, [In, Out] Modo[] modos, IntPtr topologia);
    [DllImport("user32.dll")]
    static extern int DisplayConfigGetDeviceInfo(ref NombreDestino nombre);
    [DllImport("user32.dll")]
    static extern int SetDisplayConfig(uint nRutas, Ruta[] rutas, uint nModos, Modo[] modos, uint flags);

    const uint SoloActivas = 0x2, SinModo = 0xFFFFFFFF, ModoOrigen = 1;
    const uint UsarDada = 0x20, Aplicar = 0x80, PermitirAjustes = 0x400;
    const uint TopologiaExtendida = 0x4, TopologiaGuardada = 0xF;

    public static bool EsLG(string dispositivo)
    {
        return dispositivo.IndexOf("#GSM", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool EsVirtual(string dispositivo)
    {
        return dispositivo.IndexOf("PSCCDD0", StringComparison.OrdinalIgnoreCase) >= 0; // Parsec VDD
    }

    public static Estado Leer()
    {
        for (int i = 0; i < 5; i++) // la configuración puede cambiar entre las dos llamadas
        {
            uint nr, nm;
            if (GetDisplayConfigBufferSizes(SoloActivas, out nr, out nm) != 0) break;
            var rutas = new Ruta[nr];
            var modos = new Modo[nm];
            if (QueryDisplayConfig(SoloActivas, ref nr, rutas, ref nm, modos, IntPtr.Zero) != 0) { Thread.Sleep(200); continue; }
            Array.Resize(ref rutas, (int)nr);
            Array.Resize(ref modos, (int)nm);
            return new Estado { Rutas = rutas, Modos = modos, Dispositivos = rutas.Select(Dispositivo).ToArray() };
        }
        return null;
    }

    static string Dispositivo(Ruta ruta)
    {
        if (ruta.Destino.Disponible == 0) return "";
        var n = new NombreDestino { Tipo = 2, Adaptador = ruta.Destino.Adaptador, Id = ruta.Destino.Id };
        n.Tamano = (uint)Marshal.SizeOf(typeof(NombreDestino));
        return DisplayConfigGetDeviceInfo(ref n) == 0 ? (n.Dispositivo ?? "") : "";
    }

    // Deja activas solo las pantallas que no son el LG, con la primera como principal en 0,0.
    public static int QuitarLG(Estado estado)
    {
        var rutas = new List<Ruta>();
        var modos = new List<Modo>();
        var indices = new Dictionary<uint, uint>();
        Func<uint, uint> copiar = viejo =>
        {
            if (viejo == SinModo || viejo >= estado.Modos.Length) return SinModo;
            if (!indices.ContainsKey(viejo)) { indices[viejo] = (uint)modos.Count; modos.Add(estado.Modos[viejo]); }
            return indices[viejo];
        };

        for (int i = 0; i < estado.Rutas.Length; i++)
        {
            if (estado.Dispositivos[i] == "" || EsLG(estado.Dispositivos[i])) continue;
            Ruta r = estado.Rutas[i];
            r.Origen.Modo = copiar(r.Origen.Modo);
            r.Destino.Modo = copiar(r.Destino.Modo);
            rutas.Add(r);
        }
        if (rutas.Count == 0) return -1;

        int primero = modos.FindIndex(m => m.Tipo == ModoOrigen);
        if (primero >= 0)
        {
            int dx = modos[primero].X, dy = modos[primero].Y;
            for (int i = 0; i < modos.Count; i++)
            {
                if (modos[i].Tipo != ModoOrigen) continue;
                Modo m = modos[i];
                m.X -= dx;
                m.Y -= dy;
                modos[i] = m;
            }
        }
        return Poner(rutas.ToArray(), modos.ToArray());
    }

    public static int Restaurar(Estado estado) { return Poner(estado.Rutas, estado.Modos); }

    static int Poner(Ruta[] rutas, Modo[] modos)
    {
        return SetDisplayConfig((uint)rutas.Length, rutas, (uint)modos.Length, modos, UsarDada | Aplicar | PermitirAjustes);
    }

    public static int Extender() { return SetDisplayConfig(0, null, 0, null, TopologiaExtendida | Aplicar); }
    public static int UsarGuardada() { return SetDisplayConfig(0, null, 0, null, TopologiaGuardada | Aplicar); }
}

// Una pantalla de Parsec VDD. El driver la retira si no recibe un ping por cerca de un segundo.
class PantallaVirtual
{
    const uint Agregar = 0x0022e004, Quitar = 0x0022a008, Ping = 0x0022a00c;
    static readonly IntPtr Invalido = new IntPtr(-1);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_Interface_List_SizeW(out uint largo, ref Guid guid, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_Interface_ListW(ref Guid guid, string id, [Out] char[] lista, uint largo, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string ruta, uint acceso, uint compartir, IntPtr seguridad, uint creacion, uint flags, IntPtr plantilla);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr h, uint codigo, IntPtr entrada, uint nEntrada, IntPtr salida, uint nSalida, IntPtr devueltos, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetOverlappedResultEx(IntPtr h, IntPtr ov, out uint bytes, uint ms, bool alertable);
    [DllImport("kernel32.dll")]
    static extern bool CancelIoEx(IntPtr h, IntPtr ov);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    IntPtr driver = Invalido;
    int numero;
    Thread pings;
    volatile bool parar;
    volatile int fallos;

    public bool Viva { get { return driver != Invalido && fallos < 3; } }

    public bool Crear()
    {
        Destruir();
        var guid = new Guid("00b41627-04c4-429e-a26e-0265cf50c8fa");
        uint largo;
        if (CM_Get_Device_Interface_List_SizeW(out largo, ref guid, null, 0) != 0 || largo < 2) return false;
        var lista = new char[largo];
        if (CM_Get_Device_Interface_ListW(ref guid, null, lista, largo, 0) != 0) return false;
        string ruta = new string(lista).Split('\0')[0];
        IntPtr h = CreateFile(ruta, 0xC0000000, 3, IntPtr.Zero, 3, 0x80 | 0x20000000 | 0x40000000 | 0x80000000, IntPtr.Zero);
        if (h == Invalido) return false;

        uint respuesta;
        if (!Pedir(h, Agregar, -1, 5000, out respuesta) || respuesta >= 16) { CloseHandle(h); return false; }
        driver = h;
        numero = (int)respuesta;
        fallos = 0;
        parar = false;
        pings = new Thread(() =>
        {
            while (!parar)
            {
                uint nada;
                if (Pedir(h, Ping, -1, 500, out nada)) fallos = 0; else fallos++;
                Thread.Sleep(100);
            }
        });
        pings.IsBackground = true;
        pings.Priority = ThreadPriority.Highest;
        pings.Start();
        return true;
    }

    public void Destruir()
    {
        if (driver == Invalido) return;
        parar = true;
        pings.Join();
        uint nada;
        Pedir(driver, Quitar, numero, 1000, out nada);
        Pedir(driver, Ping, -1, 500, out nada);
        CloseHandle(driver);
        driver = Invalido;
    }

    static bool Pedir(IntPtr h, uint codigo, int pantalla, uint ms, out uint respuesta)
    {
        respuesta = 0;
        int tamOv = Marshal.SizeOf(typeof(NativeOverlapped));
        IntPtr memoria = Marshal.AllocHGlobal(tamOv + 36);
        for (int i = 0; i < tamOv + 36; i++) Marshal.WriteByte(memoria, i, 0);
        IntPtr entrada = IntPtr.Add(memoria, tamOv), salida = IntPtr.Add(entrada, 32);
        if (pantalla >= 0) { Marshal.WriteByte(entrada, 0, (byte)(pantalla >> 8)); Marshal.WriteByte(entrada, 1, (byte)pantalla); }

        var evento = new ManualResetEvent(false);
        Marshal.StructureToPtr(new NativeOverlapped { EventHandle = evento.SafeWaitHandle.DangerousGetHandle() }, memoria, false);
        DeviceIoControl(h, codigo, entrada, 32, salida, 4, IntPtr.Zero, memoria);
        uint bytes;
        bool ok = GetOverlappedResultEx(h, memoria, out bytes, ms, false);
        if (!ok && Marshal.GetLastWin32Error() == 258)
        {
            CancelIoEx(h, memoria);
            // Si ni cancelado termina, la memoria queda para el driver, que todavía puede escribir en ella.
            if (!GetOverlappedResultEx(h, memoria, out bytes, 2000, false) && Marshal.GetLastWin32Error() == 258)
            {
                GC.SuppressFinalize(evento.SafeWaitHandle);
                return false;
            }
        }
        if (ok && bytes >= 4) respuesta = (uint)Marshal.ReadInt32(salida);
        if (ok && codigo == Agregar && bytes < 4) ok = false;
        evento.Dispose();
        Marshal.FreeHGlobal(memoria);
        return ok;
    }
}

// Un solo hilo hace todos los cambios de pantalla.
class Control
{
    public readonly EventWaitHandle Salir = Evento("salir"), Encender = Evento("encender"), Apagar = Evento("apagar"), Alternar = Evento("alternar");
    public volatile bool Apagado, Ocupado, Terminado;
    public volatile string Aviso;

    // Al reactivarse, el LG (DisplayPort) desaparece un momento, y al cortar ahí se vieron dos pantallas
    // virtuales. No se verificó quién creó la segunda. DeskLink crea una cuando no ve ningún monitor y
    // tiene una sesión, y la retira 2 s después de que vuelve el monitor.
    const int AsentarseMs = 3000, EsperaAsentarseMs = 15000;

    readonly System.Diagnostics.Stopwatch estable = System.Diagnostics.Stopwatch.StartNew();
    readonly PantallaVirtual virtualLG = new PantallaVirtual();
    Pantallas.Estado original;
    int fallosSeguidos;

    [DllImport("user32.dll")]
    static extern void mouse_event(uint flags, int x, int y, uint datos, UIntPtr extra);

    static EventWaitHandle Evento(string nombre)
    {
        return new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MonitorLG." + nombre);
    }

    public void Ciclo()
    {
        var eventos = new WaitHandle[] { Salir, Encender, Apagar, Alternar };
        while (true)
        {
            int cual = WaitHandle.WaitAny(eventos, 1000);
            Ocupado = cual != WaitHandle.WaitTimeout;
            try
            {
                if (cual == 0) { if (Apagado) Prender(); break; }
                if (cual == 1 || (cual == 3 && Apagado)) Prender();
                else if (cual == 2 || cual == 3) Cortar();
                else if (Apagado) Vigilar();
            }
            catch (Exception e)
            {
                Registro.Anotar("error: " + e);
                Aviso = "Error: " + e.Message;
            }
            Ocupado = false;
        }
        virtualLG.Destruir();
        Terminado = true;
    }

    void Cortar()
    {
        if (Apagado) return;
        Pantallas.Estado antes = EsperarEstable();
        original = antes != null && antes.HayLG ? antes : null;
        fallosSeguidos = 0;
        if (CortarSenal()) { Apagado = true; return; }
        virtualLG.Destruir();
        Aviso = "No pude apagar el monitor. Detalle en %TEMP%\\MonitorLG.log";
    }

    bool CortarSenal()
    {
        Pantallas.Estado e = Pantallas.Leer();
        if (e == null) return false;
        Registro.Anotar("apagar: activas=" + e.Resumen);
        if (!e.HayOtra)
        {
            if (!virtualLG.Crear()) { Registro.Anotar("Parsec VDD no creó la pantalla"); return false; }
            e = Esperar(x => x.HayOtra, 5000);
            if (e == null)
            {
                Registro.Anotar("la virtual no entró sola, extiendo: " + Pantallas.Extender());
                e = Esperar(x => x.HayOtra, 8000);
                if (e == null) return false;
            }
            Thread.Sleep(700);
            e = Pantallas.Leer();
            if (e == null) return false;
        }
        if (!e.HayLG) return true;
        int r = Pantallas.QuitarLG(e);
        Pantallas.Estado fin = Esperar(x => !x.HayLG, 5000);
        Registro.Anotar("quitar LG=" + r + " quedaron=" + (fin == null ? "LG sigue activo" : fin.Resumen));
        return fin != null;
    }

    // Vuelve a cortar si el LG reaparece en el escritorio o si ya no queda otra pantalla.
    void Vigilar()
    {
        Pantallas.Estado e = Pantallas.Leer();
        if (e == null || (!e.HayLG && e.HayOtra)) { fallosSeguidos = 0; return; }
        Registro.Anotar("vigilar: volvió a cambiar: " + e.Resumen + " virtual viva=" + virtualLG.Viva);
        if (CortarSenal()) return;
        if (++fallosSeguidos < 5) return;
        Prender();
        Aviso = "No pude mantener el monitor apagado.";
    }

    void Prender()
    {
        Apagado = false;
        if (original != null) Registro.Anotar("encender: restaurar=" + Pantallas.Restaurar(original));
        virtualLG.Destruir();
        if (Esperar(x => x.HayLG, 6000) == null)
        {
            Registro.Anotar("encender: guardada=" + Pantallas.UsarGuardada());
            if (Esperar(x => x.HayLG, 6000) == null) Aviso = "El LG no volvió. Tocá su botón de encendido.";
        }
        mouse_event(1, 1, 0, 0, UIntPtr.Zero);
        mouse_event(1, -1, 0, 0, UIntPtr.Zero);
        Pantallas.Estado fin = Pantallas.Leer();
        Registro.Anotar("encendido: activas=" + (fin == null ? "?" : fin.Resumen));
        estable.Restart();
    }

    Pantallas.Estado EsperarEstable()
    {
        Pantallas.Estado e = null;
        for (int t = 0; t < EsperaAsentarseMs; t += 250)
        {
            e = Pantallas.Leer();
            if (e == null || !e.HayLG || e.HayVirtual) estable.Restart();
            else if (estable.ElapsedMilliseconds >= AsentarseMs) return e;
            Thread.Sleep(250);
        }
        Registro.Anotar("el LG no se estabilizó: activas=" + (e == null ? "?" : e.Resumen));
        return e;
    }

    static Pantallas.Estado Esperar(Func<Pantallas.Estado, bool> listo, int ms)
    {
        for (int t = 0; t < ms; t += 250)
        {
            Thread.Sleep(250);
            Pantallas.Estado e = Pantallas.Leer();
            if (e != null && listo(e)) return e;
        }
        return null;
    }
}

class Atajo : NativeWindow, IDisposable
{
    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr h, int id, uint modificadores, uint tecla);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr h, int id);

    readonly Action accion;
    public readonly bool Registrado;

    public Atajo(Action accion)
    {
        this.accion = accion;
        CreateHandle(new CreateParams());
        Registrado = RegisterHotKey(Handle, 1, 0x1 | 0x2 | 0x4000, (uint)Keys.F10); // Alt, Ctrl, sin repetición
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312) accion();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, 1);
        DestroyHandle();
    }
}

static class Programa
{
    [STAThread]
    static int Main(string[] args)
    {
        string orden = args.Length > 0 ? args[0].TrimStart('/', '-').ToLowerInvariant() : "";
        if (args.Length > 1 || !new[] { "", "apagar", "encender", "alternar", "salir" }.Contains(orden))
        {
            MessageBox.Show("Órdenes válidas: apagar, encender, alternar, salir", "Monitor LG");
            return 2;
        }

        bool primero;
        using (var mutex = new Mutex(true, @"Local\MonitorLG.instancia", out primero))
        {
            var control = new Control();
            if (orden == "apagar") control.Apagar.Set();
            if (orden == "encender") control.Encender.Set();
            if (orden == "alternar") control.Alternar.Set();
            if (orden == "salir" && !primero) control.Salir.Set();
            if (!primero || orden == "salir") return 0;

            Bandeja(control);
            GC.KeepAlive(mutex);
        }
        return 0;
    }

    static void Bandeja(Control control)
    {
        Application.EnableVisualStyles();
        var hilo = new Thread(control.Ciclo);
        hilo.Start();

        Icon prendido = Dibujar(false), apagado = Dibujar(true);
        var menu = new ContextMenuStrip();
        var alternar = menu.Items.Add("Apagar monitor", null, (s, e) => control.Alternar.Set());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir (prende el monitor)", null, (s, e) => control.Salir.Set());

        using (var atajo = new Atajo(() => control.Alternar.Set()))
        using (var icono = new NotifyIcon { Icon = prendido, ContextMenuStrip = menu, Text = "Monitor LG", Visible = true })
        {
            icono.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left && !control.Ocupado) control.Alternar.Set(); };
            SystemEvents.SessionEnding += (s, e) => control.Salir.Set();
            Registro.Anotar("inicio: atajo Ctrl+Alt+F10 registrado=" + atajo.Registrado);

            var reloj = new System.Windows.Forms.Timer { Interval = 300 };
            reloj.Tick += (s, e) =>
            {
                if (control.Terminado) { reloj.Stop(); Application.ExitThread(); return; }
                bool off = control.Apagado;
                icono.Icon = off ? apagado : prendido;
                icono.Text = off ? "Monitor LG apagado: clic para prender" : "Monitor LG prendido: clic para apagar";
                alternar.Text = off ? "Prender monitor" : "Apagar monitor";
                string aviso = control.Aviso;
                if (aviso == null) return;
                control.Aviso = null;
                icono.ShowBalloonTip(5000, "Monitor LG", aviso, ToolTipIcon.Warning);
            };
            reloj.Start();
            Application.Run();
            icono.Visible = false;
        }
        hilo.Join();
    }

    static Icon Dibujar(bool off)
    {
        using (var imagen = new Bitmap(32, 32))
        using (var g = Graphics.FromImage(imagen))
        using (var marco = new SolidBrush(off ? Color.Gray : Color.White))
        using (var pantalla = new SolidBrush(off ? Color.FromArgb(40, 40, 40) : Color.LimeGreen))
        using (var tachado = new Pen(Color.Red, 4))
        {
            g.FillRectangle(marco, 1, 3, 30, 21);
            g.FillRectangle(pantalla, 4, 6, 24, 15);
            g.FillRectangle(marco, 13, 24, 6, 4);
            g.FillRectangle(marco, 8, 27, 16, 3);
            if (off) g.DrawLine(tachado, 4, 29, 28, 3);
            return Icon.FromHandle(imagen.GetHicon());
        }
    }
}
