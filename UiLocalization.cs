using System.Windows.Forms;

static class UiLocalization
{
    static readonly Dictionary<string,string> English = new(StringComparer.Ordinal)
    {
        ["TALLER  ·  INVENTARIO"]="WORKSHOP  ·  INVENTORY", ["Biblioteca de filamentos"]="Filament library",
        ["Bobinas, materiales y consumo de impresión"]="Spools, materials and print usage", ["＋  Nueva bobina"]="＋  New spool",
        ["BIBLIOTECA"]="LIBRARY", ["RANURAS AMS"]="AMS SLOTS", ["HISTORIAL"]="HISTORY", ["TUS BOBINAS"]="YOUR SPOOLS",
        ["Editar bobina"]="Edit spool", ["Eliminar"]="Delete", ["Selecciona una tarjeta · doble clic para editar"]="Select a card · double-click to edit",
        ["ASIGNA LAS BOBINAS CARGADAS. No hace falta NFC ni RFID."]="ASSIGN THE LOADED SPOOLS. No NFC or RFID needed.",
        ["Registrar / corregir consumo"]="Record / edit usage", ["Tu biblioteca está vacía. Añade una bobina para empezar."]="Your library is empty. Add a spool to get started.",
        ["Aún no hay impresiones registradas. Las completadas aparecerán aquí."]="No prints recorded yet. Completed prints will appear here.",
        ["NUEVA BOBINA"]="NEW SPOOL", ["DATOS DE LA BOBINA"]="SPOOL DETAILS", ["Registra el material y el peso para controlar tu inventario."]="Enter the material and weight to track your inventory.",
        ["Nombre de la bobina"]="Spool name", ["Material"]="Material", ["Marca"]="Brand", ["Peso inicial (g)"]="Starting weight (g)", ["Peso restante (g)"]="Remaining weight (g)", ["Color"]="Color",
        ["Elegir color…"]="Choose color…", ["La muestra del color se actualiza al elegirlo."]="The color swatch updates when you choose a color.", ["Cancelar"]="Cancel", ["Guardar bobina"]="Save spool",
        ["Selecciona primero una bobina."]="Select a spool first.", ["Selecciona una impresión del historial."]="Select a print from history.", ["Escribe un nombre."]="Enter a name.", ["Comprueba los pesos introducidos."]="Check the entered weights.",
        ["Sin asignar"]="Unassigned", ["Sin bobina"]="No spool", ["Vacía"]="Empty", ["Añade bobina"]="Add spool", ["Bobina cargada"]="Spool loaded",
        ["Consumo manual"]="Manual usage", ["Bobina utilizada"]="Spool used", ["Indica el consumo"]="Enter usage", ["PENDIENTE"]="PENDING", ["REGISTRADA"]="RECORDED",
        ["TEMPERATURAS"]="TEMPERATURES", ["BIBLIOTECA"]="LIBRARY", ["AJUSTES"]="SETTINGS", ["SIN CONEXIÓN"]="OFFLINE", ["SIN CONFIGURAR"]="NOT SET UP",
        ["Esperando impresora"]="Waiting for printer", ["Sin impresión activa"]="No active print", ["En espera"]="Idle", ["Imprimiendo"]="Printing", ["Preparando"]="Preparing",
        ["Finalizada"]="Finished", ["Error de impresión"]="Print error", ["En pausa"]="Paused", ["CLOUD ACTIVA"]="CLOUD CONNECTED",
        ["Esperando conexión"]="Waiting for connection", ["Actualizado"]="Updated", ["Actualizado · "]="Updated · ", ["Conectando…"]="Connecting…",
        ["Fin estimado · "]="Estimated finish · ", [" restantes"]=" remaining", [" capas"]=" layers", [" ranuras"]=" slots",
        ["Reenviar"]="Resend", ["Enviar código"]="Send code", ["Verificar código"]="Verify code", ["Conectar impresora"]="Connect printer",
        ["Conectar con Bambu Cloud"]="Connect to Bambu Cloud", ["Ajustes"]="Settings", ["Selecciona la impresora vinculada."]="Select the linked printer.",
        ["Escribe el correo de tu cuenta Bambu Lab."]="Enter your Bambu Lab account email.", ["Solicitando código a Bambu Lab…"]="Requesting code from Bambu Lab…",
        ["Código enviado. Revisa tu correo e introdúcelo aquí."]="Code sent. Check your email and enter it here.", ["Escribe el código que recibiste por correo."]="Enter the code you received by email.",
        ["Verificando código…"]="Verifying code…", ["Buscando impresoras vinculadas a tu cuenta…"]="Finding printers linked to your account…",
        ["Bambu Cloud · P1S"]="Bambu Cloud · P1S", ["No hace falta NFC ni RFID."]="No NFC or RFID needed."
    };

    public static bool IsEnglish => AppPreferences.Current.Language == "en";
    public static string T(string text)
    {
        if (!IsEnglish) return text;
        foreach (var pair in English.OrderByDescending(x => x.Key.Length)) text = text.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        text = text.Replace("bobinas", "spools", StringComparison.OrdinalIgnoreCase)
                   .Replace("consumos por revisar", "usage entries to review", StringComparison.OrdinalIgnoreCase)
                   .Replace("consumo pendiente", "pending usage", StringComparison.OrdinalIgnoreCase)
                   .Replace("g disponibles", "g available", StringComparison.OrdinalIgnoreCase)
                   .Replace("Indica el consumo", "Enter usage", StringComparison.OrdinalIgnoreCase)
                   .Replace("impresión(es) necesitan revisar el consumo", "print(s) need usage review", StringComparison.OrdinalIgnoreCase)
                   .Replace("ranura", "slot", StringComparison.OrdinalIgnoreCase)
                   .Replace("de carga", "of capacity", StringComparison.OrdinalIgnoreCase)
                   .Replace("restante", "remaining", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    public static void Apply(Control root)
    {
        if (!IsEnglish) return;
        root.Text = T(root.Text);
        foreach (Control control in root.Controls)
        {
            control.Text = T(control.Text);
            if (control is ListBox list)
                for (int i=0;i<list.Items.Count;i++) if (list.Items[i] is string s) list.Items[i]=T(s);
            if (control is ToolStrip strip)
                foreach (ToolStripItem item in strip.Items) item.Text=T(item.Text ?? string.Empty);
            Apply(control);
        }
    }
}
