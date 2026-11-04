
# BambuPulse

**Español | English**

---
<img width="526" height="638" alt="image" src="https://github.com/user-attachments/assets/ce148e89-0044-434d-a7b9-d893353a471c" />


## Español

Un widget compacto para Windows con tema oscuro que permite monitorizar el estado de tu impresora Bambu Lab directamente desde el escritorio.

### Características

- **Estado en tiempo real:** consulta el estado y el progreso de impresión mediante Bambu Cloud.
- **Modo compacto:** interfaz minimalista con opción de minimizar en la bandeja del sistema.
- **Temperaturas:** monitoriza la temperatura de la boquilla y de la cama.
- **Tiempo restante:** consulta el tiempo estimado para finalizar la impresión.
- **AMS:** visualiza información sobre las bobinas instaladas en el sistema AMS.
- **Biblioteca de filamentos:** gestiona tu colección personal de filamentos.
- **Historial de consumo:** seguimiento automático y manual del uso de filamento.
- **Solo lectura:** monitorización de la impresora sin enviar comandos de control.

### Requisitos

- Windows 10 o Windows 11.
- .NET 8 SDK.
- Una cuenta de Bambu Lab.
- Una impresora vinculada a tu cuenta de Bambu Lab.

### Ejecutar desde el código fuente

1. Clona este repositorio.
2. Abre una terminal en la carpeta del proyecto.
3. Ejecuta:

   ```bash
   dotnet run
   ```

4. Abre **Settings**.
5. Introduce el correo electrónico de tu cuenta de Bambu Lab.
6. Verifica tu cuenta mediante el código de un solo uso.
7. Selecciona tu impresora.

### Datos locales y privacidad

La aplicación almacena sus datos localmente en:

```text
%LOCALAPPDATA%\BambuP1SWidget
```

- Los archivos de datos locales están excluidos de Git mediante `.gitignore`.
- El token de acceso a la nube está protegido mediante Windows DPAPI.
- No es necesario incluir credenciales, tokens, números de serie ni información del inventario en el repositorio.
- La aplicación utiliza Bambu Cloud y MQTT para obtener información de la impresora.
- Algunos endpoints se basan en información observada por la comunidad y podrían cambiar en futuras actualizaciones.
- Las estimaciones de consumo de filamento pueden diferir del consumo real.

### Compilar

```bash
dotnet build
```

### Estado del proyecto

Este es un proyecto independiente desarrollado por la comunidad. No está afiliado, asociado ni respaldado oficialmente por Bambu Lab.

Los créditos y referencias de diseño de la interfaz se encuentran en [`DESIGN.md`](DESIGN.md).

---

## English

A compact dark-themed Windows desktop widget that lets you monitor your Bambu Lab printer directly from your desktop.

### Features

- **Live status:** monitor printer status and print progress through Bambu Cloud.
- **Compact mode:** minimal interface with an optional system tray mode.
- **Temperatures:** monitor nozzle and heatbed temperatures.
- **Remaining time:** view the estimated time left until the print is complete.
- **AMS overview:** view information about the filament spools installed in your AMS.
- **Filament library:** manage your personal filament collection.
- **Usage history:** automatically or manually track filament consumption.
- **Read-only monitoring:** monitor your printer without sending control commands.

### Requirements

- Windows 10 or Windows 11.
- .NET 8 SDK.
- A Bambu Lab account.
- A printer linked to your Bambu Lab account.

### Run from source

1. Clone this repository.
2. Open a terminal in the project folder.
3. Run:

   ```bash
   dotnet run
   ```

4. Open **Settings**.
5. Enter your Bambu Lab account email.
6. Verify your account using the one-time code.
7. Select your printer.

### Local data and privacy

The application stores its data locally in:

```text
%LOCALAPPDATA%\BambuP1SWidget
```

- Local data files are excluded from Git using `.gitignore`.
- The cloud access token is protected using Windows DPAPI.
- There is no need to include account credentials, tokens, serial numbers, or inventory information in the repository.
- The application uses Bambu Cloud and MQTT to retrieve printer information.
- Some endpoints are based on community-observed information and may change in future updates.
- Filament consumption estimates may differ from actual usage.

### Build

```bash
dotnet build
```

### Project status

This is an independent community project. It is not affiliated with, associated with, or officially endorsed by Bambu Lab.

UI design credits and references can be found in [`DESIGN.md`](DESIGN.md).
