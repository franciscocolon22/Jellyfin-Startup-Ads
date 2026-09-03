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
| Versión del plugin | **1.4.1.0** |
| Paquetes | `Jellyfin.Controller` / `Jellyfin.Model` 10.11.11 (`ExcludeAssets=runtime`) |
| Licencia | MIT |

> 📖 **Guía completa de configuración y funcionamiento** (qué hace cada opción y qué
> se ejecuta en runtime): [`docs/CONFIGURACION.md`](docs/CONFIGURACION.md)

---

## Novedad v1.4.1 — la configuración, en 3 bloques claros + carpeta del pre-roll

La página del Dashboard estaba mezclando en «Configuración general» ajustes que en
realidad son **solo de la presentación**. Ahora hay **3 bloques independientes**, cada
uno con su propio botón **Guardar**:

| Bloque | Qué contiene | A qué afecta |
|---|---|---|
| **1 · Configuración general** | Activar el plugin, Idioma, Estadísticas | A **los dos** sistemas |
| **2 · Presentación (al iniciar Jellyfin Web)** | Inyección del script, **carpeta de la presentación**, «Qué anuncios mostrar», modo de visualización, cuenta atrás / omitir, vídeo, apariencia + **lista de anuncios de la presentación** | Solo al overlay web |
| **3 · Anuncios antes de reproducir (pre-roll)** | Activar, **carpeta de vídeos del pre-roll**, a qué contenido aplica, máximo por reproducción, orden, aleatorio, frecuencia + **lista de vídeos pre-roll** | Solo al pre-roll |

**Nuevo en el pre-roll: «Carpeta de vídeos del pre-roll».** Es una carpeta del servidor
—**dentro de una biblioteca de Jellyfin**— donde tienes tus clips de anuncio. Con ella:

- **Validar carpeta** comprueba que existe y que Jellyfin tiene vídeos indexados ahí.
- **Escanear e importar vídeos** crea un pre-roll por cada vídeo de esa carpeta (y quita
  los que se importaron antes y cuyo vídeo ya no está). Los pre-rolls hechos a mano no se tocan.
- El buscador de **«Añadir pre-roll»** ya solo muestra los vídeos de esa carpeta.

Además, el proveedor de intros ahora envía también la **ruta** del vídeo
(`IntroInfo.Path`, como *Local Intros*) y **descarta al vuelo** cualquier pre-roll cuyo
vídeo se haya borrado de la biblioteca.

---

## Novedad v1.4.0 — segundo sistema de anuncios: **pre-roll antes de cada vídeo**

A partir de v1.4.0 el plugin tiene **dos sistemas de anuncios totalmente independientes**,
cada uno con su propia sección de configuración y su propia lista de anuncios:

| Sistema | Cuándo se muestra | Dónde funciona | Contenido del anuncio |
|---|---|---|---|
| **Anuncios de la presentación** (el de siempre) | Al abrir / iniciar sesión en **Jellyfin Web** | Solo navegador web (overlay inyectado) | Imagen, vídeo o texto de un directorio externo |
| **Anuncios antes de reproducir (pre-roll)** — **NUEVO** | **Antes de cada película o episodio**, al darle a reproducir | **Apps nativas incluidas** (Android APK, Android TV, Roku…) y web | Un **vídeo de tu biblioteca de Jellyfin** |

### Cómo funciona el pre-roll

Jellyfin descubre automáticamente cualquier `IIntroProvider` de un plugin
(`ApplicationHost.GetExports<IIntroProvider>()`). Cuando un cliente empieza a reproducir
una película o un episodio, Jellyfin pide a este plugin la lista de «intros» y el cliente
las reproduce **antes** del contenido — el mismo mecanismo que usa *Local Intros*, así que
funciona en las apps nativas, no solo en la web.

Jellyfin 10.11 solo acepta como intro un vídeo **que ya esté en una biblioteca**
(`LibraryManager.ResolveIntro`), por eso cada anuncio pre-roll **apunta a un ítem real de
tu biblioteca** (hay un buscador en el editor). Sube tus vídeos-anuncio a cualquier
biblioteca (por ejemplo una carpeta «Anuncios») y elígelos ahí.

### Opciones del pre-roll (Dashboard → Jellyfin Startup Ads → «Anuncios antes de reproducir»)

- **Activar el pre-roll** — interruptor independiente del overlay de la presentación.
- **Aplicar a** — Películas, Episodios, o ambos.
- **Máximo de vídeos por reproducción** (1–10).
- **Orden** — Prioridad, Nombre, Aleatorio o Manual.
- **Elegir solo uno al azar** de los que apliquen.
- **Frecuencia** — Cada reproducción / Una vez al día por usuario / Porcentaje de probabilidad.
- Por cada anuncio: **vídeo de la biblioteca**, prioridad, orden, **rango de fechas**,
  **días de la semana**, **franja horaria** y **segmentación por usuario**.

Un fallo en la selección del pre-roll **nunca interrumpe la reproducción** (se captura y
se registra en el log).

---

## Correcciones v1.3.0 – v1.3.4 (tras las pruebas en servidor real)

| Síntoma en el servidor | Causa raíz (verificada en el código de Jellyfin 10.11.11) | Corrección |
|---|---|---|
| **v1.3.4** — el contador del **vídeo** empezaba en **150** (su duración) y el de la **imagen** en 5, ignorando la config | Cada anuncio tenía su propio tiempo y los vídeos usaban su duración real (`FromVideo`). | **Un solo número global**: «Configuración general → **Duración del anuncio (segundos)**» es la cuenta regresiva de **todos** los anuncios (imagen, vídeo, texto). Un vídeo más largo se corta ahí. Se quitan del editor los campos de tiempo. |
| **v1.3.4** — «Modo de anuncios» aún parecía invertido | El filtro backend es correcto (2 tests, ambos sentidos). Confusión de datos + heurística de v1.3.3 demasiado agresiva. | Opciones del desplegable **renombradas** («Solo los que creé a mano» / «Solo los de Escanear»), **resumen en vivo** de cuántos hay de cada origen y columna **«Origen»**. Se retira la heurística de v1.3.3. Log de diagnóstico. |
| **v1.3.3** — **«Escanear e importar»** devolvía siempre `0, 0` aunque importara | El JS no parseaba el cuerpo JSON de las peticiones `POST` (`ApiClient.ajax` sin `dataType:'json'` devuelve un `Response`). Afectaba también a **«Validar ruta»**. | `req()` reescrito con `fetch` + `Authorization`, que **siempre lee y parsea** la respuesta. |
| **v1.3.3** — se **elimina «Duración (segundos)»** del editor de anuncios | Había dos números confusos. | Ver la fila de v1.3.4: ahora es **un solo número global**. |
| **v1.3.2** — el **«Modo de visualización»** solo mostraba *Modal* | El JS ponía `max-width`/`max-height` **en línea** en la tarjeta, y eso gana siempre sobre las reglas CSS de *Pantalla completa* / *Banner central*. | El tamaño va por **variables CSS** (`--sa-max-w`…) que las reglas por modo sí pueden sobreescribir. |
| **v1.3.2** — **«Reproducir sin sonido»** no silenciaba | `video.muted = true` como propiedad no basta para la decisión de autoplay en Chrome/Safari. | Se fija además `defaultMuted` y el **atributo** `muted` antes de asignar `src`. |
| **v1.3.2** — «Modo de anuncios» parecía invertido | Al **editar** un anuncio, el formulario no reenviaba `AutoGenerated` → un anuncio de *Escanear* pasaba a *Manual* al guardarlo. (El filtro backend `Manual`↔creado a mano / `Automatic`↔escaneado es correcto, con test.) | `Update` **conserva** el `AutoGenerated` existente. Nueva columna **«Origen»**. |
| **v1.3.2** — el editor mostraba campos que no aplicaban al tipo | — | El editor muestra **solo los campos del tipo elegido** y **filtra el desplegable de archivos** por tipo. |
| **v1.3.2** — enlace poco visible | `GetPages()` no marcaba el menú principal. | `EnableInMainMenu = true` → enlace directo en el menú lateral. `Plugin.cs` vuelve al constructor estándar de 2 parámetros. |
| **v1.3.1** — el overlay **nunca arrancaba**: consola con `GET …/web/StartupAds/ClientScript 404` | El `<script>` inyectado usaba `src` **relativo**; como `index.html` se sirve en `/web/`, el navegador lo resolvía a `/web/StartupAds/ClientScript`, pero el endpoint del plugin está en `/StartupAds/…` (raíz del sitio). | `src` **absoluto** `/StartupAds/ClientScript`; el prefijo de un proxy inverso (`/jellyfin/web/…`) se detecta a partir de la ruta de la petición. |
| El overlay **nunca aparecía** en Jellyfin Web; `GET /StartupAds/Config` daba **HTTP 500** | Jellyfin 10.11 **eliminó** la política con nombre `"DefaultAuthorization"`. Un `[Authorize(Policy="DefaultAuthorization")]` referencia una política inexistente → ASP.NET lanza `InvalidOperationException` → 500. Afectaba a `Config`, `Media`, `Media/Background`, `Track`. | Los endpoints de usuario usan ahora `[Authorize]` a secas (política por defecto = usuario autenticado). `GetConfig` además es tolerante a fallos: ante excepción devuelve `Enabled=false` en vez de 500. |
| El botón **«Guardar anuncio»** recargaba la página o no hacía nada; **«Cancelar»** no cerraba el modal | La página de configuración buscaba su raíz con `document.querySelector('#id')`. Jellyfin recicla **hasta 3 contenedores de página** (`pageContainerCount = 3`) → podía haber varios `#startupAdsConfigPage` y los listeners se ataban al DOM equivocado. | El script se engancha a `document`.`viewshow` y usa `e.target` (la vista viva). Guard por elemento (`view.__saInit`). |
| El modal salía **descentrado a la derecha** y los botones quedaban recortados/inalcanzables | `.mainAnimatedPages` usa `transform` para las animaciones de vista → un hijo `position:fixed` se posiciona respecto a ese ancestro, no respecto al *viewport*. | El modal se **traslada a `document.body`** al abrir; así es un overlay real del *viewport*, con cabecera y pie fijos, cuerpo con scroll y responsive. |
| (potencial) La media no cargaba en servidores con *legacy auth* desactivada | `<video>`/`<img>` usaban `?api_key=`, que en 10.11 solo funciona con `EnableLegacyAuthorization`. | Se usa `?ApiKey=` (siempre válido) y `ApiClient.ajax` (cabecera `Authorization` correcta). |
| El plugin no se identificaba bien en el **catálogo** | Sin *GitHub Release* publicada, el `sourceUrl` del manifest da 404 y el catálogo no puede resolver el paquete. La instalación manual **sin** el `meta.json` incluido hace que Jellyfin use el nombre de la carpeta. | El `meta.json` es correcto (`guid`/`name`/`version`/`targetAbi` verificados contra `PluginManifest` de 10.11.11). Hay que **publicar la Release** e instalar el ZIP **completo** (DLL + `meta.json`). Desde v1.3.2 el enlace también aparece en el menú lateral (`EnableInMainMenu`). |

Se conservan íntegras todas las correcciones de v1.1.0 (seguridad de rutas, symlinks, UNC, magic bytes, medianoche, tracking, cambio de usuario…) y de v1.2.0 (inyección en memoria).

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
| Jellyfin Web en navegador (Chrome, Edge, Firefox, Safari) | Diseñado para este caso — **pendiente de validación en servidor real** | Objetivo principal. |
| Jellyfin Media Player (escritorio) | Por validar | Embebe Jellyfin Web; debería comportarse como el navegador. |
| Apps Android / iOS | Parcial, por validar | Mayormente nativas; el overlay solo podría aparecer en las vistas web. Autoplay con sonido casi siempre bloqueado. |
| Android TV / Fire TV / Roku / Kodi / Swiftfin | No soportado | UI nativa, no ejecutan JS de Jellyfin Web. |

> Ningún cliente marcado como "por validar" ha sido probado todavía en un Jellyfin
> real. Se actualizará esta tabla tras la prueba en el servidor.

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
pwsh ./build/package.ps1              # -> artifacts/jellyfin-startup-ads_1.3.4.0.zip
```

El ZIP contiene únicamente `Jellyfin.Plugin.StartupAds.dll` y `meta.json`
(sin código fuente, tests, `.git`, README ni solución). El script imprime el
**MD5** y el tamaño y los guarda en `artifacts/release-info.json`. La compilación
es reproducible (`Deterministic` + timestamps fijos en el ZIP), por lo que el
checksum es estable.

Proceso de release:

1. `pwsh ./build/package.ps1`
2. Copiar el MD5 impreso a `manifest.json` (`checksum`).
3. `git tag v1.3.4 && git push origin v1.3.4`
4. Crear el *GitHub Release* `v1.3.4` y subir el ZIP como asset.
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
sudo mkdir -p "/var/lib/jellyfin/plugins/Jellyfin Startup Ads_1.3.4.0"
sudo unzip artifacts/jellyfin-startup-ads_1.3.4.0.zip \
     -d "/var/lib/jellyfin/plugins/Jellyfin Startup Ads_1.3.4.0"
sudo chown -R jellyfin:jellyfin "/var/lib/jellyfin/plugins/Jellyfin Startup Ads_1.3.4.0"
sudo systemctl restart jellyfin
```

> La ruta exacta es `<DataDir>/plugins/`. `<DataDir>` es `/var/lib/jellyfin` en el
> paquete oficial; en Docker suele ser `/config`. Compruébala en
> Dashboard → **Panel de control → Rutas**.

**Windows**: `%ProgramData%\Jellyfin\Server\plugins\Jellyfin Startup Ads_1.3.4.0\`
(descomprimir el ZIP ahí) y reiniciar el servicio Jellyfin.

### Verificación

Tras reiniciar: Dashboard → **Plugins** → debe aparecer **Jellyfin Startup Ads**
(estado *Active*) y abrir su **Configuración** sin errores. En el log del servidor:

```
[StartupAds] index.html injection middleware registered (in-memory, no disk changes).
[StartupAds] Client script injected into index.html response (/web/index.html).   <- al primer GET de la web
```

Comprobación rápida desde el propio servidor:

```bash
curl -s http://localhost:8096/web/index.html | grep -o 'startup-ads-inject'   # -> startup-ads-inject
```

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

## Mecanismo de inyección en Jellyfin Web (v1.3.4)

**Jellyfin core, incluida la 10.11.11, NO tiene ninguna API oficial** para que un
plugin añada un script a `jellyfin-web`. El PR que lo proponía
([jellyfin/jellyfin#9095](https://github.com/jellyfin/jellyfin/pull/9095),
`IWebFileTransformationWriteService`) fue **cerrado sin fusionar**; los mantenedores
consideran que modificar el frontend "no es algo que los plugins deban hacer".
Alternativas reales evaluadas:

| Opción | Veredicto |
|---|---|
| **(A) Middleware ASP.NET Core en memoria** (`IStartupFilter`) | **Elegida.** Ver abajo. |
| (B) Parchear `jellyfin-web/index.html` en disco (v1.0–v1.1) | Descartada: falla si `web/` es de solo lectura o propiedad de `root` (Docker), se pierde en cada actualización de Jellyfin Web, deja residuo si el plugin cae. |
| (C) Plugin externo *File Transformation* (IAmParadox27) | Descartada como dependencia: es de terceros, se invoca por reflexión y ha tenido fallos de arranque en 10.11.x. Compatible como **complemento opcional** si el usuario ya lo tiene. |
| (D) API oficial de Jellyfin | No existe en 10.11.11. |

### Cómo funciona (A)

`PluginServiceRegistrator` registra un `IStartupFilter` — un punto de extensión
**estándar de ASP.NET Core**, no una API de Jellyfin — que inserta
`IndexHtmlInjectionMiddleware` al frente del pipeline. Ese middleware:

1. detecta las peticiones `GET`/`HEAD` de `…/index.html`, `/<WebBasePath>` o `/`;
2. quita `Accept-Encoding` para recibir el HTML sin comprimir;
3. almacena la respuesta, y si es `200` + `text/html` inserta **en memoria** una
   línea antes de `</body>`:
   ```html
   <script id="startup-ads-inject" src="StartupAds/ClientScript" defer></script>
   ```
4. reescribe `Content-Length` y envía el HTML modificado.

Propiedades:

- **Cero escrituras en disco** → resistente a actualizaciones de Jellyfin Web,
  compatible con Docker y con `web/` de solo lectura o propiedad de `root`.
- **Sin paso de limpieza**: desinstalar el plugin quita el middleware; no queda
  residuo en ningún archivo.
- **Idempotente**: si el HTML ya contiene la marca, no se vuelve a inyectar.
- **Solo toca `index.html`**: cualquier otra respuesta (JS, CSS, API, 304…) pasa
  intacta.
- **A prueba de fallos**: si el middleware lanza una excepción, se sirve la
  respuesta original y Jellyfin Web sigue funcionando.
- Configurable: casilla **Inyectar el script** (on por defecto) y **Ruta base de
  Jellyfin Web** (`/web` por defecto) en la página del plugin.

El JS y el CSS los sirve el backend del plugin (`GET StartupAds/ClientScript` /
`ClientStyle`), así que actualizar el plugin actualiza el frontend.

> **Estado de validación**: el mecanismo está cubierto por **tests de integración
> del pipeline real de ASP.NET Core** (`InjectionMiddlewareTests`, `TestServer`) —
> inyecta en HTML, ignora no-HTML y API, funciona con `Accept-Encoding: gzip`, es
> idempotente. **No** está todavía verificado dentro de un Jellyfin 10.11.11 real
> (ver *Prueba real en el servidor*).

---

## Docker

Con `linuxserver/jellyfin` o la imagen oficial, `jellyfin-web` forma parte de la
capa de imagen (solo lectura). La inyección **en memoria** de v1.3.4 no toca esos
archivos, así que funciona igual que en una instalación nativa. Solo hay que
montar la carpeta de anuncios como volumen y configurar esa ruta en el plugin:

```yaml
services:
  jellyfin:
    image: jellyfin/jellyfin:10.11.11
    volumes:
      - ./config:/config
      - ./cache:/cache
      - ./media:/media
      - ./startup-ads:/startup-ads        # <- carpeta de anuncios
```

Plugins: `/config/plugins/Jellyfin Startup Ads_1.3.4.0/` (dentro del volumen
`config`). Ruta de anuncios en el plugin: `/startup-ads`.

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
| `curl …/web/index.html` no muestra `startup-ads-inject` | El middleware no interceptó: revisa el log (`middleware registered`), que `InjectClientScript` esté activo y que `WebBasePath` coincida con tu ruta base. Prueba con otro plugin de inyección (JavaScript Injector) como alternativa. |
| El script se inyecta pero el overlay no aparece | Caché del navegador: fuerza recarga (Ctrl+F5). En F12 → Network mira `GET /StartupAds/Config`: debe ser **200** (en v1.1–v1.2 daba 500). |
| Botones «Guardar anuncio» / «Cancelar» sin efecto (v1.2 y anteriores) | Corregido en **v1.3.4**. Actualiza el plugin. |
| El modal de anuncios sale a la derecha / recortado (v1.2 y anteriores) | Corregido en **v1.3.4** (el modal se traslada a `<body>`). |
| El plugin aparece con nombre raro en «Mis complementos» | Instala el **ZIP completo** (DLL + `meta.json`) en una carpeta `Jellyfin Startup Ads_1.3.4.0`, no solo el DLL. |
| El plugin no aparece en el **Catálogo** | Falta la *GitHub Release* `v1.3.4` con el ZIP subido como asset (el `sourceUrl` del manifest da 404 hasta entonces). |
| No aparece ningún anuncio | ¿`Enabled` y `ShowOnStartup`? ¿La ruta valida OK? ¿Hay anuncios activos y en horario para tu usuario? |
| El anuncio no reaparece | `FrequencyMode = OncePerSession`: cierra la pestaña o usa `EveryStartup`. |
| El vídeo no arranca solo | El navegador bloquea autoplay con sonido: mantén `MutedVideo`. |
| Docker: nada que hacer con `web/` | Correcto — v1.3.4 no toca `jellyfin-web` en disco. |
| "Nombre de archivo no válido" al guardar un anuncio | El nombre contenía `/`, `\`, `..` o `:`. Usa solo el nombre del fichero. |
| Logs | `journalctl -u jellyfin -f | grep StartupAds` (systemd) o `docker logs -f jellyfin 2>&1 | grep StartupAds`. |

---

## Limitaciones

1. Clientes nativos (Android TV, Roku, Kodi, Swiftfin) **no** soportados: no
   ejecutan Jellyfin Web.
2. **Jellyfin core no tiene API oficial de inyección** (PR #9095 cerrado). El
   mecanismo de v1.3.4 usa `IStartupFilter` (extensión estándar de ASP.NET Core).
   Si un futuro Jellyfin cambiara su forma de servir `index.html` o el orden del
   pipeline, habría que revisar `IndexHtmlInjectionMiddleware`.
3. Autoplay de vídeo **con** sonido: no es posible de forma fiable.
4. La navegación a un item usa `Dashboard.navigate` / hash routing; si Jellyfin
   cambia su router habrá que ajustar `handleAction` en `startup-ads.js`.
5. Estadísticas: solo contadores agregados, sin panel de informes.
6. **Qué está probado y qué no** (ver también *Prueba real en el servidor*):
   - ✅ *Test unitario / de build* (`TEST DE BUILD` + `TEST UNITARIO`): `dotnet build`
     0 warnings / 0 errors, `dotnet test` **95/95** en verde. Cubre: selección de
     anuncios, horarios (incl. medianoche `22:00→02:00`), seguridad de rutas
     (traversal, UNC, symlink, magic bytes), tracking, `IndexHtmlInjector`, y el
     **contrato de autorización** de la API (`ApiAuthorizationTests` — falla si vuelve
     a aparecer `"DefaultAuthorization"`).
   - ✅ *Test de integración de pipeline* (`TEST DE INTEGRACIÓN`): 6 tests con
     `Microsoft.AspNetCore.TestHost` que ejercitan el middleware de inyección sobre un
     pipeline ASP.NET Core **real** (no Jellyfin).
   - ✅ *Packaging*: ZIP reproducible (`d9bea84ffd35ddf7a7301ad9579840b3`, 47 965 B),
     solo `Jellyfin.Plugin.StartupAds.dll` + `meta.json` (verificado: sin
     `Jellyfin.Controller.dll` / `Jellyfin.Model.dll`).
   - ⚠️ *Análisis contra el código fuente de Jellyfin 10.11.11*: los bugs de v1.3.4 se
     localizaron leyendo `jellyfin` y `jellyfin-web` v10.11.11 (policies, `viewContainer`,
     `PluginManifest`, `AuthorizationContext`). Las correcciones se derivan de ese código,
     pero **no** sustituyen a una prueba real.
   - ❌ *Test real en Jellyfin 10.11.11* (`TEST REAL EN JELLYFIN`): **NO realizado** en
     este entorno (no hay servidor Jellyfin).
   - ❌ *Test real en navegador* (`TEST REAL EN NAVEGADOR`): **NO realizado**
     (Chrome / Edge / Firefox sin probar).

---

## Prueba real en el servidor (checklist)

> Este proyecto **no** puede declararse "producción" hasta completar esta lista en
> el servidor Jellyfin 10.11.11 real.

### Procedimiento

```
BUILD      pwsh ./build/package.ps1
           # -> artifacts/jellyfin-startup-ads_1.3.4.0.zip  (MD5 en release-info.json)

UPLOAD     scp artifacts/jellyfin-startup-ads_1.3.4.0.zip usuario@servidor:/tmp/

INSTALL    ssh usuario@servidor
           sudo mkdir -p "/var/lib/jellyfin/plugins/Jellyfin Startup Ads_1.3.4.0"
           sudo unzip -o /tmp/jellyfin-startup-ads_1.3.4.0.zip \
                -d "/var/lib/jellyfin/plugins/Jellyfin Startup Ads_1.3.4.0"
           sudo chown -R jellyfin:jellyfin "/var/lib/jellyfin/plugins/Jellyfin Startup Ads_1.3.4.0"

RESTART    sudo systemctl restart jellyfin

VALIDATE   systemctl status jellyfin --no-pager
           journalctl -u jellyfin -b --no-pager | grep -Ei 'StartupAds|error|exception'
           find /var/lib/jellyfin/plugins -iname '*StartupAds*'
           curl -s http://localhost:8096/web/index.html | grep -o startup-ads-inject
```

### Checklist

**Plugin**
- [ ] Aparece en Dashboard → Plugins, estado *Active*
- [ ] `journalctl` sin `Unhandled exception` / `TypeLoadException` / `MissingMethodException` del plugin
- [ ] Se ve `[StartupAds] index.html injection middleware registered`

**Inyección**
- [ ] `curl …/web/index.html | grep startup-ads-inject` → coincide
- [ ] `GET …/web/main.*.js` NO contiene la marca
- [ ] Tras `sudo apt upgrade jellyfin-web` (o nueva imagen Docker) sigue inyectando sin re-instalar

**Configuración**
- [ ] La página de configuración abre sin errores de consola
- [ ] Se guarda el directorio de anuncios; **Validar ruta** responde
- [ ] Crear / editar / duplicar / activar-desactivar / eliminar anuncio
- [ ] **Escanear** importa archivos; borrar un archivo + escanear elimina su anuncio auto
- [ ] **Vista previa** muestra el overlay

**Archivos**: probar JPG, PNG, WEBP, MP4, WEBM válidos + un ejecutable renombrado a `.png` (debe rechazarse)

**Jellyfin Web** (navegador): abrir, login, logout, cambio de usuario, recarga,
navegar, reproducir contenido, volver al dashboard — el overlay aparece cuando toca
y **nunca** deja a Jellyfin inutilizable.

**Anuncio**: aparece · countdown 5→4→3→2→1 · *Omitir* en el momento correcto ·
imagen se ve · vídeo se reproduce y termina · texto correcto · botón `ExternalUrl`
abre pestaña · botón `JellyfinItem` navega · click se registra (con estadísticas on).

**Usuarios**: anónimo (sin overlay) · autenticado · cambio de usuario · logout→login
· recarga · varias pestañas · cerrar y reabrir navegador · incógnito.

**Seguridad** (con un token de usuario normal, no admin):
```bash
TOKEN=...; H="Authorization: MediaBrowser Token=$TOKEN"
curl -s -o /dev/null -w '%{http_code}\n' -H "$H" http://localhost:8096/StartupAds/Config            # 200
curl -s -o /dev/null -w '%{http_code}\n'         http://localhost:8096/StartupAds/Config            # 401
curl -s -o /dev/null -w '%{http_code}\n' -H "$H" http://localhost:8096/StartupAds/Admin/Advertisements  # 403
curl -s -o /dev/null -w '%{http_code}\n' -H "$H" http://localhost:8096/StartupAds/Track/<adId>/hack     # 400
curl -s -o /dev/null -w '%{http_code}\n' -H "$H" "http://localhost:8096/StartupAds/Media/<adIdDeOtroUsuario>"  # 403/404
```

---

## Actualización a futuras versiones de Jellyfin

1. Subir `Jellyfin.Controller` / `Jellyfin.Model` al nuevo número.
2. Ajustar `TargetFramework` si cambia el runtime.
3. Actualizar `targetAbi` en `manifest.json`, `build.yaml` y `build/meta.json`.
4. Verificar que siguen existiendo: `IHasWebPages`, `IPluginServiceRegistrator`,
   `ILibraryManager.GetItemById`, las policies `DefaultAuthorization` /
   `RequiresElevation`, el claim `Jellyfin-UserId`, y que el host sigue respetando
   `IStartupFilter` registrado por plugins.
5. Comprobar que `IndexHtmlInjectionMiddleware` sigue interceptando `…/web/index.html`
   (los tests de `InjectionMiddlewareTests` cubren el pipeline; validar en servidor real).
6. `dotnet test` y prueba manual con la lista de *Prueba real en el servidor*.

---

## Arquitectura (resumen)

```
Jellyfin Server (10.11.11 / .NET 9)
├── Plugin.cs / PluginServiceRegistrator.cs
├── Configuration/  PluginConfiguration.cs · Advertisement.cs · configPage.html
├── Api/StartupAdsController.cs   (anónimo / usuario / RequiresElevation)
├── Services/
│   ├── MediaFileService.cs        validación de ruta, symlinks, firma, enumeración
│   └── AdvertisementManager.cs    CRUD · escaneo · Select() puro · schedule · tracking
├── ClientInjection/
│   ├── IndexHtmlInjector.cs              helper puro (inserta/idempotente)
│   ├── IndexHtmlInjectionMiddleware.cs   reescribe la respuesta de index.html en memoria
│   └── StartupAdsStartupFilter.cs        IStartupFilter que registra el middleware
└── Web/  startup-ads.js · startup-ads.css   (servidos por el backend)
```
