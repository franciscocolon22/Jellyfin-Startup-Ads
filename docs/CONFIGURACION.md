# Jellyfin Startup Ads — Guía de configuración y funcionamiento

> Aplica a la versión **1.3.1** del plugin · Jellyfin **10.11.11** · .NET **9**

Este documento explica, campo por campo, **para qué sirve cada opción**, **qué valor
tiene por defecto** y, sobre todo, **qué ocurre en tiempo de ejecución cuando la
opción está activa**.

---

## Índice

1. [Qué es y para qué sirve](#1-qué-es-y-para-qué-sirve)
2. [Arquitectura en 30 segundos](#2-arquitectura-en-30-segundos)
3. [El ciclo de vida completo (qué se ejecuta y cuándo)](#3-el-ciclo-de-vida-completo)
4. [Configuración general](#4-configuración-general)
5. [Configuración de un anuncio](#5-configuración-de-un-anuncio)
6. [Cómo se elige qué anuncio se muestra](#6-cómo-se-elige-qué-anuncio-se-muestra)
7. [La cuenta regresiva y el botón «Omitir», paso a paso](#7-la-cuenta-regresiva-y-el-botón-omitir-paso-a-paso)
8. [Estadísticas: qué evento se registra y cuándo](#8-estadísticas)
9. [Persistencia: dónde se guarda todo](#9-persistencia)
10. [API interna del plugin](#10-api-interna-del-plugin)
11. [Seguridad aplicada a la configuración](#11-seguridad-aplicada-a-la-configuración)
12. [Preguntas frecuentes de comportamiento](#12-preguntas-frecuentes-de-comportamiento)

---

## 1. Qué es y para qué sirve

**Jellyfin Startup Ads** muestra uno o varios **anuncios / avisos multimedia** (imagen,
vídeo o texto) en un *overlay* que aparece **encima de Jellyfin Web justo al abrirlo**,
antes de que el usuario empiece a navegar. Sirve para:

- avisos internos de un servidor familiar/comunitario (mantenimiento, normas, novedades);
- promoción de contenido recién añadido (con botón que lleva a la ficha en Jellyfin);
- campañas con fecha/hora de inicio y fin;
- mensajes dirigidos a usuarios concretos.

El contenido vive en una **carpeta del servidor que tú eliges** (no dentro del plugin),
y todo se administra desde **Dashboard → Complementos → Jellyfin Startup Ads**.

**Dónde funciona:** solo donde se renderiza *Jellyfin Web* (navegador, Jellyfin Media
Player). Las apps nativas (Android TV, Roku, Kodi, Swiftfin…) **no** lo ejecutan.

---

## 2. Arquitectura en 30 segundos

```
Jellyfin Server (backend C#/.NET 9)
│
├── Middleware de inyección ──► reescribe la respuesta de /web/index.html EN MEMORIA
│                               y le añade:  <script src="/StartupAds/ClientScript" defer>
│                               (no toca ningún archivo del disco)
│
├── API REST  /StartupAds/...   Config · Media · Track · Admin/*
│
└── PluginConfiguration.xml     toda la config + la lista de anuncios + estadísticas
                                (se guarda solo; sobrevive a reinicios)

Navegador (Jellyfin Web)
│
└── startup-ads.js  ──►  espera sesión ► pide anuncios ► pinta overlay ► countdown ►
                         Omitir/Acción/Cerrar ► limpieza total
```

---

## 3. El ciclo de vida completo

### 3.1 Al arrancar el servidor Jellyfin

1. El plugin se carga. En el log verás:
   `[StartupAds] Plugin loaded. Name='Jellyfin Startup Ads' Id=6d1a9b6e... Version=1.3.1.0`
2. Se registra el middleware de inyección:
   `[StartupAds] index.html injection middleware registered (in-memory, no disk changes).`

### 3.2 Cuando un navegador pide la web

3. La primera petición a `…/web/index.html` (o `/web/` o `/`) pasa por el middleware.
   Si **`Activar inyección`** está encendido y la respuesta es HTML `200`, el middleware
   inserta antes de `</body>`:
   ```html
   <script id="startup-ads-inject" src="/StartupAds/ClientScript" defer></script>
   ```
   Log (la primera vez): `[StartupAds] Client script injected into index.html response (...)`.
4. El navegador descarga `/StartupAds/ClientScript` → es el `startup-ads.js` del plugin.

### 3.3 En el navegador, al cargar la página

5. El script se ejecuta **una sola vez por carga completa de página**
   (guard `window.__startupAdsLoaded`).
6. **Espera** a que Jellyfin esté listo: comprueba cada **500 ms**, hasta **40 veces
   (~20 s)**, que exista `ApiClient` y que haya **sesión iniciada**. No hay *polling*
   infinito: si a los ~20 s no hay sesión, deja de sondear y solo reacciona a eventos
   de navegación.
7. En cuanto hay usuario autenticado → llama a **`evaluate()`**.

### 3.4 `evaluate()` — la función que decide si se muestra algo

`evaluate()` se ejecuta:
- al terminar la espera del punto 6, y
- **en cada cambio de vista** de Jellyfin (`viewshow`) — pero es baratísima y **solo
  hace algo si cambia el usuario**.

Lógica:

| Paso | Comprobación | Resultado |
|---|---|---|
| a | ¿Hay `ApiClient` y sesión? | Si no → guarda `lastUserId = null` y espera. |
| b | `userId` actual vs. `lastUserId` (ya procesado en esta carga) | Si es el mismo → **no hace nada** (evita repetir en la misma página). |
| c | Cambió el usuario (o es la 1.ª vez) | Cierra cualquier overlay del usuario anterior. |
| d | `GET /StartupAds/Config` (para **ese** usuario) | El backend devuelve settings + lista de anuncios ya filtrada. |
| e | `Enabled` falso o lista vacía | **No muestra nada.** |
| f | `FrequencyMode = OncePerSession` | Mira `sessionStorage["startupAds:shown:{userId}"]`. Si ya está → **no muestra**. Si no → lo marca. |
| g | Hay anuncios que mostrar | Inyecta el CSS del overlay y **arranca la cola** de anuncios. |

**Traducción práctica de cuándo aparece el anuncio:**

- ✅ Al **abrir o recargar** Jellyfin Web con sesión iniciada.
- ✅ **Justo después de iniciar sesión**.
- ✅ Al **cambiar de usuario** sin recargar.
- ✅ Tras **logout → login** en la misma pestaña.
- ❌ Al navegar entre vistas (Inicio, biblioteca, ficha, ajustes, reproducir…).
- ❌ Otra vez en la misma pestaña si `FrequencyMode = OncePerSession` (hasta cerrar la
  pestaña o abrir otra nueva).

### 3.5 Mostrar un anuncio (`showAd`)

Para **cada** anuncio de la cola:

1. Se elimina cualquier overlay previo y se crea uno nuevo, **añadido a `<body>`**
   (para que sea un overlay real de pantalla y no lo afecten las animaciones de Jellyfin).
2. Se monta el contenido según el **tipo**:
   - **Vídeo** → `<video>` con `autoplay`/`muted`/`loop`/`controls` según la config global.
   - **Imagen** → `<img>`.
   - **Texto** → solo título + descripción (con `object-fit` para el fondo si lo hay).
   - **Multimedia** → imagen o vídeo (se detecta por la extensión) + título + descripción + botón.
3. Título y descripción se pintan con `textContent` → **el HTML se muestra escapado**, nunca se ejecuta.
4. Si el anuncio tiene **botón** (`ButtonText` + `ButtonAction ≠ None`) se añade.
5. Se añade el pie con el botón **Omitir** y, si `ShowCloseButton` está activo, una **X**.
6. Se registra el evento **`impression`** (si las estadísticas están activas).
7. Arranca la **máquina de estados de la cuenta regresiva** (ver §7).
8. Si es vídeo con `autoplay`, se intenta reproducir; si el navegador lo bloquea, se
   reintenta en silencio; si aun así falla, se usa la duración manual.

### 3.6 Cierre y limpieza (`cleanup`)

Se dispara al pulsar **Omitir**, la **X**, **ESC** (si está permitido), al terminar la
duración sin *Omitir*, o al abandonar la página (`pagehide`). Hace, **en este orden**:

1. Marca el anuncio como terminado (idempotente: no se ejecuta dos veces).
2. Para todos los *timers* (`setInterval` del contador + `setTimeout` de fin).
3. `video.pause()` → quita el `src` → `video.load()` (libera memoria/red).
4. Quita todos los *event listeners* añadidos (teclado, etc.).
5. Anima la salida (*fade-out* ~220 ms) y **elimina el nodo del DOM**.
6. **Restaura el foco** al elemento que había antes de abrir el overlay.
7. Pasa al **siguiente anuncio de la cola** (o termina).

Resultado: **cero fugas** de nodos, *timers* o *listeners*; Jellyfin queda 100 % usable.

### 3.7 Si algo falla

- La API devuelve error / no hay red → el overlay **no se muestra**, Jellyfin sigue igual.
- Una imagen o un vídeo no cargan → se maneja el error y el anuncio se cierra o pasa al siguiente.
- El backend lanza una excepción al construir la config → devuelve `Enabled:false` (no un 500).
- El middleware de inyección falla → sirve el `index.html` original sin tocar.

> **Regla de oro:** un fallo del plugin **nunca** debe romper Jellyfin.

---

## 4. Configuración general

**Dashboard → Complementos → Jellyfin Startup Ads.** Al terminar, pulsa **Guardar**.

### 4.1 Activación

| Opción | Por defecto | Qué hace cuando está activa / en runtime |
|---|---|---|
| **Activar anuncios** (`Enabled`) | ✅ | Interruptor maestro. Si está **apagado**, `GET /StartupAds/Config` responde `Enabled:false` y el frontend **no muestra nada** (ni evalúa anuncios). |
| **Mostrar anuncios al iniciar Jellyfin Web** (`ShowOnStartup`) | ✅ | Igual que el anterior a efectos prácticos: si está apagado, no se muestra ningún anuncio en el arranque. Se separa de `Enabled` para poder dejar el plugin activo (API, config) pero sin overlay. |
| **Inyectar el script del plugin en Jellyfin Web** (`InjectClientScript`) | ✅ | Si está **apagado**, el middleware **no** añade el `<script>` a `index.html` → el overlay no puede cargarse. Úsalo para desactivar el frontend sin desinstalar. |
| **Ruta base de Jellyfin Web** (`WebBasePath`) | `/web` | Segmento donde Jellyfin sirve la web. Casi nunca hay que tocarlo. El prefijo de un **proxy inverso** (`https://host/jellyfin/…`) se **detecta automáticamente**; no lo pongas aquí. |

### 4.2 Origen y selección de anuncios

| Opción | Valores | Efecto en runtime |
|---|---|---|
| **Ruta de anuncios** (`AdsDirectory`) | ruta absoluta del servidor | Carpeta con las imágenes/vídeos. Ej. Linux `/var/lib/jellyfin/startup-ads`, Windows `D:\Jellyfin\StartupAds`. El botón **Validar ruta** comprueba: que existe, que Jellyfin puede leerla, que no es una carpeta del sistema ni una ruta de red (UNC), y cuántos archivos compatibles contiene. |
| **Modo de anuncios** (`SourceMode`) | `Manual` · `Automatic` · `Mixed` (por defecto **Mixed**) | Filtra qué anuncios entran en la selección: **Manual** = solo los creados a mano; **Automatic** = solo los generados por *Escanear*; **Mixed** = ambos. |
| **Orden** (`OrderMode`) | `Priority` · `Name` · `Random` · `Manual` | Cómo se ordena la cola de anuncios elegibles (ver §6). En **Priority**, *número mayor = se muestra primero*. |
| **Frecuencia** (`FrequencyMode`) | `OncePerSession` · `EveryStartup` | **Una vez por sesión**: se muestra una sola vez por pestaña/usuario (clave en `sessionStorage`); cerrar la pestaña "reinicia" la sesión. **En cada inicio**: se muestra en **cada** carga completa de la página. |
| **Modo de visualización** (`DisplayMode`) | `Modal` · `Fullscreen` · `CenterBanner` | Tamaño/forma del overlay: **Modal** (tarjeta centrada, por defecto), **Pantalla completa** (ocupa todo el *viewport*), **Banner central** (tarjeta más baja). |
| **Duración predeterminada** (`DefaultDurationSeconds`) | `10` (1–600) | Segundos que dura un anuncio **si el propio anuncio no especifica una duración** propia. |
| **Máximo de anuncios por inicio** (`MaxAdsPerStartup`) | `1` (1–20) | Cuántos anuncios como máximo se muestran seguidos en una misma apertura. Con `3`, se muestran hasta 3 en cola. |
| **Mostrar un único anuncio aleatorio** (`RandomPick`) | ⬜ | Si está activo y hay más de un anuncio elegible, se **elige uno al azar** y se ignora `MaxAdsPerStartup`. |

### 4.3 Cuenta regresiva y botón «Omitir» (valores globales)

Estos son los **valores por defecto**; cada anuncio puede sobrescribir `AllowSkip`,
`SkipAfterSeconds` y `ShowCountdown`.

| Opción | Por defecto | Efecto |
|---|---|---|
| **Mostrar cuenta regresiva** (`ShowCountdown`) | ✅ | Muestra el texto `Omitir en N` con la cuenta atrás. Si se apaga, el botón dice solo `Omitir` (deshabilitado hasta que toque). |
| **Permitir omitir** (`AllowSkip`) | ✅ | Si se apaga, **no hay botón Omitir**: el anuncio se cierra solo al agotar su duración. |
| **Permitir omitir después de** (`SkipAfterSeconds`) | `5` (0–600) | Segundos que deben pasar antes de que *Omitir* funcione. `0` = se puede omitir de inmediato. |
| **Comportamiento del botón Omitir** (`SkipButtonMode`) | `DisabledUntilCountdown` | **Visible pero deshabilitado**: el botón se ve como `Omitir en N` y se activa al llegar a 0. **Aparece solo al terminar**: el botón está **oculto** hasta que se cumple `SkipAfterSeconds`. |
| **Mostrar botón cerrar (X)** (`ShowCloseButton`) | ⬜ | Añade una **X** en la esquina que cierra el anuncio (cuenta como *skipped*). |
| **Permitir cerrar con la tecla ESC** (`AllowCloseWithEscape`) | ✅ | ESC cierra el anuncio **respetando** la restricción de *Omitir*: si aún no se puede omitir, ESC no hace nada. |

### 4.4 Vídeo (valores globales)

| Opción | Por defecto | Efecto en `<video>` |
|---|---|---|
| **Reproducir automáticamente** (`AutoplayVideo`) | ✅ | `autoplay`. Si el navegador lo bloquea, se reintenta con `muted`. |
| **Reproducir sin sonido** (`MutedVideo`) | ✅ | `muted`. **Recomendado**: los navegadores solo permiten *autoplay* sin sonido. Con sonido, el vídeo casi siempre quedará pausado hasta que el usuario interactúe. |
| **Repetir vídeo** (`LoopVideo`) | ⬜ | `loop`. Con esto, el evento `ended` **no** cierra el anuncio (se repite hasta agotar la duración o pulsar Omitir). |
| **Mostrar controles del vídeo** (`ShowVideoControls`) | ⬜ | `controls` (barra de reproducción). |

### 4.5 Apariencia

| Opción | Por defecto | Efecto |
|---|---|---|
| **Opacidad del fondo** (`OverlayOpacity`) | `0.85` (0–1) | Oscurecimiento del fondo detrás de la tarjeta (`0` = transparente, `1` = negro total). |
| **Ancho máximo** (`MaxWidthPx`) | `900` (200–6000) | Ancho máximo de la tarjeta en modo Modal / Banner. |
| **Alto máximo** (`MaxHeightPx`) | `700` (200–6000) | Alto máximo de la tarjeta. |
| **Radio de bordes** (`BorderRadiusPx`) | `14` (0–80) | Redondeo de las esquinas de la tarjeta. |
| **Color de acento** (`AccentColor`) | `#00a4dc` | Color de los botones activos (`#RRGGBB`). Si pones un valor no válido, se usa el de Jellyfin. |
| **Ajuste imagen/vídeo** (`ObjectFit`, global) | `contain` | `contain` = se ve entera sin recortar; `cover` = rellena el hueco recortando. Cada anuncio tiene su propio ajuste; este es el valor por defecto. |
| **Idioma de los textos** (`Language`) | `es` | `es` o `en`. Afecta solo a los textos del overlay: *Omitir en / Omitir / Cerrar*. |

### 4.6 Estadísticas

| Opción | Por defecto | Efecto |
|---|---|---|
| **Activar estadísticas** (`EnableStatistics`) | ⬜ | Si está activo, el frontend envía eventos (`impression`, `started`, `completed`, `skipped`, `clicked`) a `POST /StartupAds/Track/...` y el backend los **acumula por anuncio** en la config. Si está apagado, no se registra nada. Ver §8. |

### 4.7 Botones de la página que no son "opciones"

| Botón | Qué hace |
|---|---|
| **Validar ruta** | Comprueba `AdsDirectory` sin guardar y muestra el resultado. |
| **Escanear e importar archivos** | Recorre la carpeta y: (a) **crea un anuncio automático** por cada archivo compatible nuevo; (b) **elimina** los anuncios automáticos cuyo archivo ya no existe. Los anuncios creados a mano **no se tocan**. |
| **Crear anuncio** | Abre el editor (§5). |
| **Vista previa** (por anuncio) | Muestra el overlay de ese anuncio en el propio Dashboard, sin cerrar sesión. |

---

## 5. Configuración de un anuncio

Botón **Crear anuncio** (o **Editar**). El editor está dividido en secciones. Los
campos con `*` son obligatorios. La validación ocurre **en el navegador y otra vez en
el servidor**.

### 5.1 Información

| Campo | Obligatorio | Notas |
|---|---|---|
| **Nombre interno** (`Name`) | ✅ | Solo para identificarlo en la tabla. No se muestra al usuario. |
| **Tipo** (`Type`) | ✅ | `Imagen` · `Vídeo` · `Texto` · `Multimedia`. Determina qué se renderiza y qué campos hacen falta. |
| **Título** (`Title`) | — | Se muestra en grande sobre/bajo el contenido. Texto plano. |
| **Descripción** (`Description`) | — | Texto plano bajo el título (respeta saltos de línea). El HTML se muestra escapado. |

### 5.2 Archivo / contenido *(oculto para el tipo Texto)*

| Campo | Notas |
|---|---|
| **Archivo** (`MediaFile`) | Desplegable con los archivos **de la ruta configurada** que superan la validación de firma. Obligatorio para `Imagen` y `Vídeo`. Solo se acepta el **nombre del archivo** (nunca una ruta). |
| **Imagen de fondo opcional** (`BackgroundFile`) | Imagen de la misma carpeta que se usa como fondo de la tarjeta (útil para anuncios de texto). |
| **Ajuste de la imagen/vídeo** (`ObjectFit`) | `contain` o `cover` para **este** anuncio. |

### 5.3 Configuración

| Campo | Por defecto | Efecto en runtime |
|---|---|---|
| **Duración** (`DurationMode`) | `Manual` | **Configurada manualmente** = usa `DurationSeconds`. **Duración del vídeo** = espera a `loadedmetadata` y usa la duración real del vídeo; si el vídeo falla, cae a `DurationSeconds`. |
| **Duración (segundos)** (`DurationSeconds`) | `10` (1–600) | Cuánto dura el anuncio. Si es `0` o no se pone, se usa la **duración predeterminada** global. |
| **Prioridad** (`Priority`) | `5` (0–1000) | Solo cuenta si el **Orden** global es `Priority`. **Número mayor = se muestra antes.** |
| **Orden manual** (`Order`) | `0` | Solo cuenta si el **Orden** global es `Manual` (ascendente). También desempata en `Priority`. |
| **Activo** (`Enabled`) | ✅ (nuevos) | Si está desactivado, el anuncio **nunca** se selecciona. |
| **Mostrar al iniciar** (`ShowOnStartup`) | ✅ | Si se desactiva, este anuncio no entra en el arranque (pero sigue "activo" para vista previa/edición). |
| **Permitir omitir este anuncio** (`AllowSkip`) | ✅ | Sobrescribe el valor global para este anuncio. |
| **Permitir omitir después de** (`SkipAfterSeconds`) | `5` | Sobrescribe el valor global. |
| **Mostrar contador en este anuncio** (`ShowCountdown`) | ✅ | Sobrescribe el valor global. |

> El valor efectivo de *Omitir* es `AllowSkip` del anuncio **Y** `AllowSkip` global
> (ambos deben permitirlo). Igual con el contador.

### 5.4 Acción (botón del anuncio)

| Campo | Notas |
|---|---|
| **Texto del botón** (`ButtonText`) | Si está vacío o la acción es `Sin botón`, **no se muestra ningún botón**. |
| **Acción** (`ButtonAction`) | `Sin botón` (`None`) · `Abrir URL externa` (`ExternalUrl`) · `Abrir contenido de Jellyfin` (`JellyfinItem`) · `Solo cerrar el anuncio` (`CloseOnly`). |
| **URL externa** (`ButtonUrl`) | Solo con `ExternalUrl`. **Debe** empezar por `http://` o `https://` (se rechazan `javascript:`, `data:`, `file:`, `vbscript:`…). Al pulsar: abre una **pestaña nueva** y el overlay **sigue abierto** (para poder omitir). |
| **Id de contenido de Jellyfin** (`ButtonItemId`) | Solo con `JellyfinItem`. El servidor valida que **el item exista** en la biblioteca. Al pulsar: **navega a la ficha** del contenido dentro de Jellyfin y cierra el overlay. |
| `CloseOnly` | Al pulsar: simplemente cierra el anuncio (cuenta como *clicked*). |

Cómo obtener el *Id* de un contenido: abre la película/serie en Jellyfin y cópialo de
la URL (`.../details?id=`**`xxxxxxxx`**`...`).

### 5.5 Programación

Todo es **opcional**. Se evalúa con la **hora local del servidor**.

| Campo | Efecto |
|---|---|
| **Fecha de inicio** (`StartDate`) | Antes de esta fecha, **no se muestra**. |
| **Fecha de finalización** (`EndDate`) | Después de esta fecha, **no se muestra**. |
| **Hora de inicio / Hora de finalización** (`StartTime` / `EndTime`, `HH:mm`) | Ventana horaria diaria. **Si la hora final es menor que la inicial, la franja cruza medianoche**: `22:00 → 02:00` coincide con 22:00, 23:59, 00:00, 01:59 y 02:00, pero **no** con 02:01 ni con las 12:00. |
| **Días de la semana** (`DaysOfWeek`) | Casillas Dom–Sáb. Vacío = **todos los días**. |

### 5.6 Usuarios

| Campo | Efecto |
|---|---|
| **Usuarios** (`AllowedUserIds`) | Casillas con todos los usuarios de Jellyfin. **Sin selección = todos**. Con usuarios marcados: solo ellos ven el anuncio **y** solo ellos pueden descargar su media (`GET /StartupAds/Media/{id}` responde `403` a un usuario no incluido). |

---

## 6. Cómo se elige qué anuncio se muestra

Cuando el frontend llama a `GET /StartupAds/Config`, el backend ejecuta esta cadena
(método `AdvertisementManager.Select`), **en este orden exacto**:

```
1. ¿"Activar anuncios" y "Mostrar al iniciar" (globales)?   ── no ─► lista vacía
2. Filtro por "Modo de anuncios":
      Manual    → solo anuncios creados a mano
      Automatic → solo anuncios de "Escanear"
      Mixed     → todos
3. Se quedan solo los que tienen  Enabled = true  Y  ShowOnStartup = true
4. Programación:  dentro de StartDate/EndDate,  día de la semana permitido,
   y dentro de la ventana horaria (incluida la que cruza medianoche)
5. Segmentación:  AllowedUserIds vacío  Ó  contiene el userId actual
6. Contenido disponible:  tipo Texto  Ó  el archivo existe, está dentro de la
   carpeta, y su firma (magic bytes) coincide con la extensión
7. Ordenación:
      Priority → prioridad DESC, luego Order ASC
      Name     → alfabético
      Manual   → Order ASC
      Random   → barajado aleatorio
8. Si "Mostrar un único anuncio aleatorio" y quedan ≥ 2  → se elige 1 al azar
9. Se recorta a "Máximo de anuncios por inicio"
```

El frontend recibe la lista ya lista y muestra los anuncios **en ese orden, uno tras
otro** (cada uno con su cuenta regresiva; al terminar/omitir uno, empieza el siguiente).

---

## 7. La cuenta regresiva y el botón «Omitir», paso a paso

Al mostrarse un anuncio se calcula:

- `permitirOmitir` = `AllowSkip` del anuncio **y** global
- `omitirTras` = `SkipAfterSeconds` (segundos hasta poder omitir)
- `duracionTotal` = duración del vídeo (si `Duración = del vídeo`) **o** `DurationSeconds`
  (o la duración predeterminada global)

Cada **250 ms** se refresca el pie según este cuadro:

| Situación | `SkipButtonMode = DisabledUntilCountdown` | `SkipButtonMode = AppearsAfterCountdown` |
|---|---|---|
| `permitirOmitir = false` | botón **oculto**; el anuncio se cerrará solo al llegar a `duracionTotal` | botón **oculto** |
| Aún no han pasado `omitirTras` segundos | botón **visible y deshabilitado**: `Omitir en N` (o solo `Omitir` si el contador está apagado) | botón **oculto** |
| Ya pasaron `omitirTras` segundos | botón **habilitado**: `Omitir` (con color de acento) | botón **aparece habilitado**: `Omitir` |

Eventos:

- **Se agota `duracionTotal`** → se registra `completed`.
  - Si `permitirOmitir = true` → el overlay **se queda** con el botón *Omitir* activo (el usuario decide cuándo cerrarlo).
  - Si `permitirOmitir = false` → se cierra automáticamente.
- **Vídeo que termina** (`ended`, sin `loop`) → igual que agotar la duración.
- **El usuario pulsa Omitir** → `skipped` → cierre y limpieza (§3.6).
- **El usuario pulsa la X** → `skipped` → cierre.
- **ESC** (si `AllowCloseWithEscape` y ya se puede omitir) → `skipped` → cierre.
- **El usuario pulsa el botón de acción** → `clicked` → abre URL / navega al item / cierra.

Accesibilidad: el overlay es `role="dialog"` con `aria-modal`, atrapa el foco con Tab /
Shift+Tab, y al cerrarse devuelve el foco al elemento anterior.

---

## 8. Estadísticas

Solo si **Activar estadísticas** está encendido. Conjunto **cerrado** de eventos
(cualquier otro valor → `400`):

| Evento | Cuándo se envía | Notas |
|---|---|---|
| `impression` | Al mostrarse el overlay del anuncio | **Uno por visualización** (deduplicado). |
| `started` | Al empezar a reproducirse el vídeo (`playing`); en imagen/texto, al montarse | Uno por visualización. |
| `completed` | Al agotarse la duración o terminar el vídeo | **Nunca se cuenta dos veces**, aunque coincidan el `setTimeout` de fin y el evento `ended`. |
| `skipped` | Al pulsar Omitir / X / ESC | |
| `clicked` | Al pulsar el botón de acción | |

El backend valida en cada `POST /StartupAds/Track/{adId}/{kind}` que: el evento es
válido, el anuncio existe, está activo (`Enabled` + `ShowOnStartup`) y el usuario está
segmentado. Los contadores se acumulan por anuncio en la configuración del plugin.
No se guardan datos personales.

---

## 9. Persistencia

- **Todo** (opciones generales, lista de anuncios y contadores de estadísticas) se
  guarda en el sistema de configuración de plugins de Jellyfin:
  `…/plugins/configurations/Jellyfin.Plugin.StartupAds.xml`.
- Persiste tras **reiniciar Jellyfin** y tras **actualizar el plugin**.
- Al **desinstalar** el plugin, Jellyfin conserva ese XML; si lo reinstalas, recupera
  tu configuración. Para empezar de cero, borra ese archivo con Jellyfin parado.
- El `<script>` inyectado en `index.html` **no** se guarda en disco: se recompone en
  memoria en cada respuesta, así que una actualización de Jellyfin Web no lo rompe.

---

## 10. API interna del plugin

| Método | Ruta | Autorización | Uso |
|---|---|---|---|
| GET | `/StartupAds/ClientScript` | anónimo | El `startup-ads.js` (lo carga el `<script>` inyectado). |
| GET | `/StartupAds/ClientStyle` | anónimo | El CSS del overlay. |
| GET | `/StartupAds/Config` | usuario | Settings públicos + anuncios activos para ese usuario. |
| GET | `/StartupAds/Media/{adId}` | usuario | Stream del archivo del anuncio (con *range*). |
| GET | `/StartupAds/Media/{adId}/Background` | usuario | Imagen de fondo del anuncio. |
| POST | `/StartupAds/Track/{adId}/{kind}` | usuario | Registrar un evento de estadística. |
| GET/POST | `/StartupAds/Admin/Configuration` | **administrador** | Leer / guardar la configuración general. |
| GET/POST/DELETE | `/StartupAds/Admin/Advertisements[...]` | **administrador** | CRUD de anuncios (+ `/Duplicate`, `/Enabled/{bool}`). |
| POST | `/StartupAds/Admin/ValidatePath` | **administrador** | Validar la ruta de anuncios. |
| GET | `/StartupAds/Admin/Files` | **administrador** | Listar archivos compatibles de la ruta. |
| POST | `/StartupAds/Admin/Scan` | **administrador** | Escanear e importar. |
| GET | `/StartupAds/Admin/Preview?adId=` | **administrador** | Datos para la vista previa. |

Los endpoints `Admin/*` exigen rol **Administrador** (`RequiresElevation`); un usuario
normal solo puede **recibir** anuncios, nunca modificarlos.

---

## 11. Seguridad aplicada a la configuración

- **Ruta de anuncios:** solo rutas absolutas; se rechazan rutas de red (UNC `\\…`) y
  directorios del sistema (`/etc`, `/proc`, `/sys`, `/dev`, `/root`, `C:\Windows`,
  `C:\Program Files`…).
- **Nombres de archivo:** solo el nombre simple; se rechazan `/`, `\`, `..`, `:` y rutas
  absolutas. Un *symlink* dentro de la carpeta que apunte fuera se detecta y se rechaza.
- **Firma de archivo:** además de la extensión, se comprueban los *magic bytes*; un
  ejecutable renombrado a `.png` no se lista ni se sirve.
- **URLs de botón:** solo `http://` / `https://`.
- **Item de Jellyfin:** se valida que exista en la biblioteca.
- **Validación doble:** todo límite numérico y de formato se valida en el navegador y
  **otra vez en el backend** (`DurationSeconds` 1–600, `OverlayOpacity` 0–1, color `#hex`,
  idioma `es`/`en`, etc.).
- **XSS:** el overlay nunca usa `innerHTML` con datos del servidor.
- **Caché:** `ClientScript`/`ClientStyle` son cacheables (iguales para todos); `Config`
  y la media van con `private` (nunca se sirve el contenido de un usuario a otro).

---

## 12. Preguntas frecuentes de comportamiento

**Acabo de abrir Jellyfin y no salió nada.**
Comprueba, por orden: (1) que tienes la **v1.3.1** instalada y reiniciaste Jellyfin;
(2) `Ctrl+Shift+R` en el navegador; (3) F12 → Network: `StartupAds/ClientScript` = 200 y
`StartupAds/Config` = 200 con `"Ads":[…]`; (4) que hay **al menos un anuncio activo**
(créalo o pulsa *Escanear*) y que la **ruta valida OK**; (5) que el anuncio está en
fecha/hora/día y no está limitado a otro usuario; (6) si `Frecuencia = Una vez por
sesión`, prueba en una **pestaña nueva**.

**¿Cada cuánto vuelve a salir?**
Con `En cada inicio`: en cada carga completa de la página. Con `Una vez por sesión`:
una vez por pestaña; al cerrarla y abrir otra, vuelve.

**Cambié de usuario y volvió a salir.**
Correcto: al cambiar de usuario se reevalúa con la configuración del nuevo usuario.

**Puse un vídeo pero arranca en pausa / sin sonido.**
Los navegadores solo permiten *autoplay* **sin sonido**. Deja **Reproducir sin sonido**
activado. Con sonido, el usuario tendría que darle a play.

**Borré un archivo de la carpeta.**
El anuncio automático correspondiente desaparece la próxima vez que pulses *Escanear*.
Un anuncio manual cuyo archivo falta simplemente **no se muestra** (se salta).

**¿Afecta el plugin al rendimiento cuando no hay anuncios?**
Prácticamente no: una petición ligera a `Config` al abrir la web y nada más. Sin
*polling*, sin *timers* de fondo, sin descargar vídeos hasta que se van a mostrar.

**¿Funciona en la app de Android TV / Roku / Kodi?**
No. Solo donde se renderiza Jellyfin Web (navegador, Jellyfin Media Player).
