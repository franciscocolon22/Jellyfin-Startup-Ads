# Jellyfin Startup Ads

Plugin de producción para **Jellyfin Server 10.11.11** que permite **administrar y
mostrar anuncios multimedia al iniciar Jellyfin Web**, usando contenido de un
**directorio externo configurable**. Soporta imágenes, vídeos y texto, cuenta
regresiva, botón *Omitir*, programación (fechas / horas / días, incluyendo franjas
que cruzan medianoche), segmentación por usuario, prioridades, reproducción
aleatoria, botones de acción, estadísticas opcionales y administración completa
desde el Dashboard, con controles de seguridad adecuados.

| | |
|---|---|
| Jellyfin | **10.11.11** (targetAbi `10.11.0.0`) |
| .NET | **9.0** |
| Versión del plugin | **1.1.0.0** |
| Paquetes | `Jellyfin.Controller` / `Jellyfin.Model` 10.11.11 (`ExcludeAssets=runtime`) |
| Licencia | MIT |

---

## Características

- **Overlay** modal, pantalla completa o banner central; diseño oscuro, responsive,
  animaciones suaves, `object-fit` configurable.
- **Tipos de anuncio**: `Image`, `Video`, `Text`, `Multimedia`.
- **Cuenta regresiva** + **Omitir** con dos modos: botón deshabilitado durante la
  cuenta, o botón que aparece al terminar.
- **Duración**: manual o *duración del vídeo* (`FromVideo`).
- **Programación** por anuncio: `StartDate`, `EndDate`, `DaysOfWeek`, `StartTime`,
  `EndTime`. Las franjas que cruzan medianoche (p. ej. `22:00 → 02:00`) se evalúan
  correctamente.
- **Segmentación**: `AllowedUserIds` (vacío = todos los usuarios).
- **Orden**: `Priority` (número mayor = más prioridad), `Name`, `Manual`, `Random`.
- **RandomPick** (un único anuncio al azar entre los elegibles) y
  **MaxAdsPerStartup** (tope por apertura).
- **Frecuencia**: `OncePerSession` (por usuario) o `EveryStartup`.
- **Botones**: `None`, `ExternalUrl` (solo `http`/`https`), `JellyfinItem`
  (validado contra la biblioteca), `CloseOnly`.
- **Directorio externo** configurable + escaneo/importación + limpieza de anuncios
  cuyos archivos se han borrado.
- **Estadísticas** opcionales: `impression`, `started`, `completed`, `skipped`,
  `clicked` (conjunto cerrado; nunca más de un `completed` por visualización).
- **Vista previa** desde el Dashboard sin cerrar sesión.

---

## Compatibilidad por cliente

Solo los clientes que renderizan **Jellyfin Web** (una webview con `index.html`)
ejecutan el script del plugin.

| Cliente | Soporte | Nota |
|---|---|---|
| Jellyfin Web (Chrome, Edge, Firefox, Safari) | ✅ | Objetivo principal. |
| Jellyfin Media Player (escritorio) | ✅ | Embebe Jellyfin Web. |
| Apps Android / iOS | ⚠️ Parcial | Mayormente nativas; el overlay solo aparece donde se renderiza Jellyfin Web. Autoplay con sonido casi siempre bloqueado. |
| Android TV / Fire TV / Roku / Kodi / Swiftfin | ❌ | UI nativa, no ejecutan JS de Jellyfin Web. |

**Autoplay de vídeo**: se usa `autoplay + muted` (política de los navegadores); el
sonido automático no es posible de forma fiable en ningún navegador.

---

## Requisitos

- Jellyfin Server **10.11.11** en Linux (Ubuntu Server) o Windows.
- Para compilar: **.NET SDK 9.0**.
- Una carpeta del servidor con imágenes/vídeos.

---

## Compilación

```bash
dotnet restore
dotnet build -c Release
dotnet test  -c Release
```

DLL resultante: `Jellyfin.Plugin.StartupAds/bin/Release/net9.0/Jellyfin.Plugin.StartupAds.dll`
(el plugin **no** incluye ensamblados de Jellyfin: `ExcludeAssets=runtime`).

### Empaquetado (ZIP instalable)

```powershell
pwsh ./build/package.ps1              # -> artifacts/jellyfin-startup-ads_1.1.0.0.zip
```

El ZIP contiene únicamente `Jellyfin.Plugin.StartupAds.dll` y `meta.json`
(sin código fuente, tests, `.git`, README ni solución). El script imprime el
**MD5** y el tamaño y los guarda en `artifacts/release-info.json`. La compilación
es reproducible (`Deterministic` + timestamps fijos en el ZIP), por lo que el
checksum es estable.

Proceso de release:

1. `pwsh ./build/package.ps1`
2. Copiar el MD5 impreso a `manifest.json` (`checksum`).
3. `git tag v1.1.0 && git push origin v1.1.0`
4. Crear el *GitHub Release* `v1.1.0` y subir el ZIP como asset.
5. Commit de `manifest.json`.

---

## Instalación en Jellyfin 10.11.11

### Opción A — repositorio de plugins (recomendada)

Dashboard → **Plugins** → **Repositorios** → añadir:

```
https://raw.githubusercontent.com/franciscocolon22/Jellyfin-Startup-Ads/main/manifest.json
```

Luego **Catálogo** → *Jellyfin Startup Ads* → **Instalar** → reiniciar Jellyfin.

### Opción B — instalación manual del ZIP

**Linux** (paquete `.deb`/`systemd`, ruta de datos por defecto):

```bash
sudo mkdir -p "/var/lib/jellyfin/plugins/Jellyfin Startup Ads_1.1.0.0"
sudo unzip artifacts/jellyfin-startup-ads_1.1.0.0.zip \
     -d "/var/lib/jellyfin/plugins/Jellyfin Startup Ads_1.1.0.0"
sudo chown -R jellyfin:jellyfin "/var/lib/jellyfin/plugins/Jellyfin Startup Ads_1.1.0.0"
sudo systemctl restart jellyfin
```

> La ruta exacta es `<DataDir>/plugins/`. `<DataDir>` es `/var/lib/jellyfin` en el
> paquete oficial; en Docker suele ser `/config`. Compruébala en
> Dashboard → **Panel de control → Rutas**.

**Windows**: `%ProgramData%\Jellyfin\Server\plugins\Jellyfin Startup Ads_1.1.0.0\`
(descomprimir el ZIP ahí) y reiniciar el servicio Jellyfin.

### Verificación

Tras reiniciar: Dashboard → **Plugins** → debe aparecer **Jellyfin Startup Ads**
(estado *Active*) y abrir su **Configuración** sin errores. El log del servidor
muestra `[StartupAds] Plugin starting; ensuring client script injection.` y, si
Jellyfin puede escribir en `jellyfin-web`, `Client script injected into .../index.html`.

### Carpeta de anuncios

```bash
sudo mkdir -p /var/lib/jellyfin/startup-ads
sudo chown jellyfin:jellyfin /var/lib/jellyfin/startup-ads
# copiar aquí: bienvenida.jpg, promocion.mp4, ...
```

Configuración del plugin → **Ruta de anuncios** → `/var/lib/jellyfin/startup-ads`
→ **Validar ruta** → **Guardar** → **Escanear e importar archivos**.

Ejemplos de ruta: Linux `/var/lib/jellyfin/startup-ads`, `/media/anuncios`;
Windows `D:\Jellyfin\StartupAds`.

---

## Configuración

### General
`Enabled`, `ShowOnStartup`, `AdsDirectory`, `SourceMode` (Manual/Automatic/Mixed),
`OrderMode`, `FrequencyMode`, `DisplayMode`, `DefaultDurationSeconds` (1–600),
`MaxAdsPerStartup` (1–20), `RandomPick`.

### Cuenta regresiva / Omitir
`ShowCountdown`, `AllowSkip`, `SkipAfterSeconds` (0–600),
`SkipButtonMode` (`DisabledUntilCountdown` | `AppearsAfterCountdown`),
`ShowCloseButton`, `AllowCloseWithEscape`.

### Vídeo
`AutoplayVideo`, `MutedVideo`, `LoopVideo`, `ShowVideoControls`.

### Apariencia
`OverlayOpacity` (0–1), `MaxWidthPx` (200–6000), `MaxHeightPx` (200–6000),
`BorderRadiusPx` (0–80), `ObjectFit` (`contain`/`cover`), `AccentColor` (`#RRGGBB`),
`Language` (`es`/`en`).

### Estadísticas
`EnableStatistics` (opt-in).

Todos los límites se validan **también en el backend**; el frontend nunca es la
única barrera.

---

## Creación de anuncios

**Crear anuncio** → nombre interno (obligatorio), tipo, título, descripción (texto
plano; el HTML se muestra escapado), archivo (desplegable con los ficheros de la
ruta), fondo opcional, `ObjectFit`, duración (manual / del vídeo), prioridad,
orden, estado, `AllowSkip` + segundos, contador, botón (texto + acción + URL/ItemId),
programación (fechas, horas `HH:mm`, días) y usuarios objetivo.

Acciones sobre cada anuncio: **Editar**, **Activar/Desactivar**, **Duplicar**,
**Vista previa**, **Eliminar**.

---

## Cuenta regresiva y botón Omitir

- **DisabledUntilCountdown**: el botón se muestra como `Omitir en N` y se habilita
  (`Omitir`) al alcanzar `SkipAfterSeconds`.
- **AppearsAfterCountdown**: el botón está oculto hasta `SkipAfterSeconds`.
- Si `AllowSkip = false`, el overlay se cierra solo al acabar la duración.
- Al pulsar Omitir / X / ESC: limpieza inmediata — `video.pause()`, se libera el
  `src`, se limpian `setInterval`/`setTimeout` y todos los listeners, se restaura
  el foco y el nodo se elimina del DOM.

---

## Programación

`StartDate`/`EndDate` (fuera de rango → no se muestra), `DaysOfWeek` (vacío =
todos), `StartTime`/`EndTime` en hora local del servidor. Si `EndTime` es anterior
a `StartTime` la franja se interpreta como **cruce de medianoche**: `22:00 → 02:00`
coincide con 22:00, 23:59, 00:00, 01:59 y 02:00, pero no con 02:01. Cubierto por
tests (`SchedulingTests.MidnightCrossingWindow`).

---

## Usuarios

`AllowedUserIds` vacío = todos. Con usuarios seleccionados, solo ellos ven el
anuncio **y** solo ellos pueden descargar su media (`GET StartupAds/Media/{id}`
devuelve `403` a un usuario no segmentado). `OncePerSession` usa una clave por
usuario (`startupAds:shown:{userId}` en `sessionStorage`): que el usuario A vea el
anuncio no impide que el usuario B lo vea en la misma pestaña.

---

## Estadísticas

Contadores agregados por anuncio en la configuración del plugin. Eventos válidos
(conjunto cerrado, cualquier otro valor devuelve `400`): `impression`, `started`,
`completed`, `skipped`, `clicked`. El frontend garantiza **un solo `completed` por
visualización** aunque coincidan `timeout` y `video ended`.

---

## Seguridad

- **Rutas de archivos** (`MediaFileService`):
  - solo se aceptan **nombres de fichero planos** (sin `/`, `\`, `..`, `:`, sin
    ruta absoluta, sin caracteres inválidos); la entrada inválida se **rechaza
    explícitamente** (`400`), no se reescribe en silencio;
  - el candidato debe ser hijo directo del directorio configurado;
  - **symlinks**: se resuelve la ruta canónica (destino final) y se comprueba que
    siga dentro del directorio real; un symlink que escapa se rechaza;
  - el directorio configurado no puede ser una **ruta UNC** (`\\...`) ni un
    **directorio del sistema** (`/etc`, `/proc`, `/sys`, `/dev`, `/root`, `/bin`,
    `/lib`, `/run`, `C:\Windows`, `C:\Program Files`, …);
  - **validación de contenido**: además de la extensión permitida, se comprueba la
    **firma (magic bytes)** del archivo — un ejecutable/script renombrado a `.png`
    se descarta.
- **API**:
  - anónimo: solo `ClientScript` / `ClientStyle` (estáticos, iguales para todos);
  - usuario autenticado: `Config`, `Media`, `Media/Background`, `Track` — todos
    aplican **las mismas** comprobaciones (anuncio existe, `Enabled`,
    `ShowOnStartup`, usuario segmentado);
  - administrador (`RequiresElevation`): todo lo de `Admin/` (CRUD, configuración,
    escaneo, validación de ruta). Un usuario normal **no** puede modificar
    anuncios, configuración, estadísticas ni rutas.
- **Botones**: `ExternalUrl` solo admite `http://` / `https://` (se rechazan
  `javascript:`, `data:`, `file:`, `vbscript:`, …); `JellyfinItem` valida que el
  `ItemId` exista en la biblioteca.
- **XSS**: el frontend nunca usa `innerHTML` con datos del servidor; título y
  descripción se pintan con `textContent`.
- **Cache**: `ClientScript`/`ClientStyle` → `public, max-age=3600`;
  `Media`/`Background` → `private` (nunca se cachea contenido de un usuario para
  otro).
- No se registran tokens ni contraseñas en el log.

---

## Mecanismo de inyección en Jellyfin Web

Jellyfin 10.11.11 sigue sirviendo `jellyfin-web` como ficheros estáticos desde
`IServerApplicationPaths.WebPath` y **no** ofrece un hook oficial para añadir un
script al cliente. Alternativas evaluadas:

- **(A) Edición controlada de `index.html`** — *elegida*.
- (B) Depender del plugin externo *File Transformation* — descartada: añade una
  dependencia dura de terceros y un segundo punto de fallo.
- (C) Branding/CSS — insuficiente: el CSS no ejecuta lógica.

`ScriptInjectionHostedService` inserta **una sola línea** antes de `</body>`:

```html
<script id="startup-ads-inject" src="StartupAds/ClientScript" defer></script>
```

- **idempotente**: nunca se inserta dos veces (marca `startup-ads-inject`);
- **reversible**: se elimina en `StopAsync` (parada/desinstalación), y **solo**
  esa línea (regex sobre la marca); nunca se restaura una copia antigua del
  archivo;
- **tolerante**: si `index.html` no existe, o es de solo lectura, o no hay
  permisos de escritura, o se está escribiendo de forma concurrente → se registra
  un aviso y **Jellyfin sigue funcionando**;
- una **actualización de Jellyfin Web** regenera un `index.html` limpio; el
  servicio vuelve a añadir la línea en el siguiente arranque.

El JavaScript y el CSS los sirve el propio backend del plugin
(`GET StartupAds/ClientScript` / `ClientStyle`), de modo que actualizar el plugin
actualiza el frontend sin tocar ningún fichero del sistema.

Si el proceso Jellyfin no tiene permiso de escritura sobre `jellyfin-web`
(frecuente si `web/` es propiedad de `root`), el plugin **no** puede inyectar el
script. Solución: `sudo chown -R jellyfin:jellyfin /usr/share/jellyfin/web` o
servir el `web/` desde una ruta escribible por Jellyfin.

---

## Frontend: ciclo de vida

- Un único bootstrap por carga completa de página (`window.__startupAdsLoaded`).
- Espera acotada (poll de 500 ms, máx. ~20 s) hasta que hay un `ApiClient`
  autenticado; **sin polling infinito**.
- `evaluate()` se ejecuta también en cada `viewshow` (navegación SPA) — es barato
  y solo hace una petición cuando el **usuario cambia**. Cubre: login después de
  cargar la página, cambio de usuario, logout + nuevo login. Si el usuario cambia
  mientras hay un anuncio en pantalla, ese anuncio se cierra.
- `pagehide` cierra y limpia cualquier overlay.

---

## Solución de problemas

| Síntoma | Causa / solución |
|---|---|
| El plugin no aparece en el Dashboard | ZIP en la carpeta equivocada; comprobar `<DataDir>/plugins/` y reiniciar. |
| No aparece ningún anuncio | ¿`Enabled` y `ShowOnStartup`? ¿Reiniciaste Jellyfin tras instalar? ¿La ruta valida OK? ¿Hay anuncios activos y en horario? |
| Log: "No write permission on jellyfin-web/index.html" | Jellyfin no puede escribir en `web/`; ver sección de inyección. |
| El anuncio no reaparece | `FrequencyMode = OncePerSession`: cierra la pestaña o usa `EveryStartup`. |
| El vídeo no arranca solo | El navegador bloquea autoplay con sonido: mantén `MutedVideo`. |
| Tras actualizar Jellyfin Web desaparece | Normal: reinicia Jellyfin y el plugin reinyecta la línea. |
| "Nombre de archivo no válido" al guardar un anuncio | El nombre contenía `/`, `\`, `..` o `:`. Usa solo el nombre del fichero. |
| Sale a medias en el móvil | Las apps móviles son nativas salvo algunas vistas web; comportamiento esperado. |

---

## Limitaciones

1. Clientes nativos (Android TV, Roku, Kodi, Swiftfin) **no** soportados.
2. La inyección modifica `index.html`; requiere permiso de escritura sobre
   `jellyfin-web` y se pierde (y se reinyecta) con cada actualización de Jellyfin
   Web. No es una API oficial.
3. Autoplay de vídeo **con** sonido: no es posible de forma fiable.
4. La navegación a un item usa `Dashboard.navigate` / hash routing; si Jellyfin
   cambia su router habrá que ajustar `handleAction` en `startup-ads.js`.
5. Estadísticas: solo contadores agregados, sin panel de informes.
6. **No verificado en este entorno**: instalación y arranque en un servidor
   Jellyfin 10.11.11 real, la inyección sobre un `index.html` real y el
   comportamiento en navegador. Sí verificado: compilación contra los ensamblados
   reales de 10.11.11, `targetAbi` correcto, ausencia de ensamblados de Jellyfin
   en la salida, y 61 tests en verde.

---

## Actualización a futuras versiones de Jellyfin

1. Subir `Jellyfin.Controller` / `Jellyfin.Model` al nuevo número.
2. Ajustar `TargetFramework` si cambia el runtime.
3. Actualizar `targetAbi` en `manifest.json`, `build.yaml` y `build/meta.json`.
4. Verificar que siguen existiendo: `IHasWebPages`, `IPluginServiceRegistrator`,
   `IServerApplicationPaths.WebPath`, `ILibraryManager.GetItemById`, las policies
   `DefaultAuthorization` / `RequiresElevation`, el claim `Jellyfin-UserId`.
5. Revisar el nombre/ubicación de `web/index.html` y el router del cliente.
6. `dotnet test` y prueba manual con la lista de abajo.

---

## Pruebas manuales

1. Instalar, reiniciar, configurar ruta, crear un anuncio de imagen → abrir
   Jellyfin Web: aparece el overlay automáticamente.
2. Contador: `5 → 4 → 3 → 2 → 1 → Omitir`.
3. Antes del tiempo: *Omitir* deshabilitado (u oculto según el modo).
4. Después del tiempo: *Omitir* habilitado.
5–8. Un anuncio de cada tipo: Imagen / Vídeo / Texto / Multimedia.
9. Botón `ExternalUrl` → abre pestaña nueva.
10. Botón `JellyfinItem` → navega a la ficha del contenido.
11. Usuario autorizado → ve el anuncio.
12. Usuario no autorizado → no lo ve; `GET StartupAds/Media/{id}` → 403.
13. Programación normal (horario diurno).
14. Programación `22:00 → 02:00` a las 23:30 y a las 03:00.
15. `EveryStartup`: recargar la página → reaparece.
16. `OncePerSession`: navegar → no reaparece; pestaña nueva → sí.
17. Cambiar de usuario sin recargar → se reevalúan los anuncios.
18. Borrar un archivo del directorio y **Escanear** → su anuncio auto se elimina.
19. Archivo inválido (ejecutable renombrado a `.png`) → no se lista ni se sirve.
20. Reiniciar Jellyfin con el plugin instalado → arranca sin excepciones.

---

## Arquitectura (resumen)

```
Jellyfin Server (10.11.11 / .NET 9)
├── Plugin.cs / PluginServiceRegistrator.cs
├── Configuration/  PluginConfiguration.cs · Advertisement.cs · configPage.html
├── Api/StartupAdsController.cs   (anónimo / usuario / RequiresElevation)
├── Services/
│   ├── MediaFileService.cs        validación de ruta, symlinks, firma, enumeración
│   ├── AdvertisementManager.cs    CRUD · escaneo · Select() puro · schedule · tracking
│   └── ScriptInjectionHostedService.cs   <script> en index.html (idempotente/reversible)
└── Web/  startup-ads.js · startup-ads.css   (servidos por el backend)
```
