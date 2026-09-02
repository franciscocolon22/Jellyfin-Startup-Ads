# Jellyfin Startup Ads

Sistema de **anuncios / presentaciones multimedia** que aparecen automáticamente
al abrir **Jellyfin Web**. No es un banner: es un *overlay* modal / pantalla
completa con imagen, vídeo o texto, cuenta regresiva, botón *Omitir* configurable,
programación por fechas/horas, segmentación por usuario y panel de administración
en el Dashboard.

---

## 1. Análisis técnico

### Versión objetivo y ABI

| Elemento | Valor | Motivo |
|---|---|---|
| Jellyfin objetivo | **10.10.x** (probado con 10.10.7) | Rama estable actual con API de plugins madura. |
| `targetAbi` | `10.10.0.0` | ABI mínima; el plugin sólo usa API estable presente también en 10.11. |
| .NET | **net8.0** | Runtime de Jellyfin 10.10. Para 10.11 (preview, .NET 9) recompilar cambiando `TargetFramework` y el paquete `Jellyfin.Controller`. |
| Paquetes | `Jellyfin.Controller`, `Jellyfin.Model` (10.10.7) | Paquetes NuGet oficiales para plugins. |

### Mecanismo de inyección en Jellyfin Web

Jellyfin **no** expone en 10.10/10.11 una API oficial para inyectar JS/CSS en el
cliente web. Las opciones reales son:

1. **Editar `web/index.html` añadiendo un `<script>`** — enfoque usado hoy por la
   mayoría de plugins que tocan la UI (Jellyscrub, Home Screen Sections, Media Bar…).
2. Plugin externo *File Transformation* (IAmParadox) como dependencia — más potente
   pero añade una dependencia de terceros y no siempre está disponible en el catálogo.
3. Servir un tema/branding CSS — sólo CSS, insuficiente aquí (necesitamos lógica).

**Decisión:** opción 1. Un `IHostedService` (`ScriptInjectionHostedService`) añade
al arrancar una sola línea antes de `</body>`:

```html
<script id="startup-ads-inject" src="StartupAds/ClientScript" defer></script>
```

Propiedades del enfoque:

- **Idempotente**: si la marca ya está, no se vuelve a insertar.
- **Reversible**: se elimina en `StopAsync` (parada/desinstalación del plugin) y,
  además, cualquier actualización de Jellyfin Web regenera `index.html` limpio.
- **Sin dependencias**: no requiere plugins de terceros.
- **A prueba de fallos**: si no encuentra `index.html` (despliegues *headless*),
  registra un aviso y no rompe nada.

El `<script>` cargado es servido por el propio backend del plugin
(`GET /StartupAds/ClientScript`) desde un recurso embebido, de modo que actualizar
el plugin actualiza el frontend sin tocar ficheros del sistema.

### Ciclo de vida de Jellyfin Web (arranque y SPA)

Jellyfin Web es una SPA: tras la carga inicial navega cambiando *views* sin recargar.
Por eso:

- El bootstrap **no** depende de `window.onload` para la lógica de negocio: usa un
  *poll* corto (400 ms, máx. ~48 s) hasta que `ApiClient` existe y hay sesión
  autenticada. Es un único poll acotado, no un bucle permanente.
- El script se ejecuta **una sola vez por carga completa de página** (guard
  `window.__startupAdsLoaded`). Las navegaciones SPA no re‑ejecutan el `<script>`,
  así que no hay riesgo de anuncios duplicados por navegación.
- *"Una vez por sesión"* se implementa con `sessionStorage` por usuario
  (`startupAds:shown:<userId>`). `sessionStorage` se borra al cerrar la pestaña →
  volver a abrir Jellyfin muestra el anuncio de nuevo; navegar no.
- Cambio de usuario sin recargar: el backend siempre filtra por el usuario del token
  de la petición `GET /StartupAds/Config`, y la clave de sesión incluye el `userId`.

### Seguridad

- **Rutas**: todo acceso a disco pasa por `MediaFileService`. `ResolveFile` acepta
  **sólo nombres de fichero planos** (sin `/`, `\`, `..`, sin ruta absoluta),
  valida extensión y comprueba con `Path.GetFullPath` que el resultado cae *dentro*
  del directorio configurado. `ValidateDirectory` rechaza directorios del sistema
  (`/etc`, `/proc`, `C:\Windows`…).
- **API**: endpoints de usuario con `Policy = "DefaultAuthorization"`; endpoints de
  administración con `Policy = "RequiresElevation"`. El streaming de medios verifica
  además que el usuario esté segmentado por el anuncio.
- **XSS**: el frontend nunca usa `innerHTML` con datos del servidor; título y
  descripción se pintan con `textContent`. El backend recorta longitudes y sólo
  admite URLs `http(s)` para el botón.
- **No se registran datos sensibles** en el log.

### Rendimiento

- Sin *polling* de 100 ms, sin `MutationObserver`, sin bucles infinitos.
- El vídeo usa `preload="auto"` **sólo** cuando el overlay se va a mostrar; si no
  hay anuncios, no se descarga nada.
- Timers (`setInterval` de 250 ms para el contador, un `setTimeout` de fin) se
  limpian por completo en el *cleanup*, junto con listeners y el elemento `<video>`
  (`pause()`, `removeAttribute('src')`, `load()`).

---

## 2. Arquitectura propuesta

```
Jellyfin Server
   │
   ├── Plugin backend (C# / .NET 8)
   │     ├── Plugin.cs .......................... metadatos + página de config
   │     ├── PluginConfiguration.cs ............. estado persistente (XML)
   │     ├── PluginServiceRegistrator.cs ........ DI
   │     ├── Services/
   │     │     ├── MediaFileService.cs ......... validación de ruta + enumeración segura
   │     │     ├── AdvertisementManager.cs ..... CRUD + escaneo + selección "activos ahora"
   │     │     └── ScriptInjectionHostedService  inyecta <script> en index.html
   │     └── Api/StartupAdsController.cs ........ endpoints REST
   │
   └── Jellyfin Web
         └── startup-ads.js  (servido por el backend, inyectado en index.html)
               ├── espera sesión autenticada
               ├── GET /StartupAds/Config  → settings + lista de anuncios
               ├── construye overlay (imagen | vídeo | texto | multimedia)
               ├── cuenta regresiva + botón Omitir (2 modos)
               ├── botón de acción (URL externa | item Jellyfin | cerrar)
               └── cleanup total
```

### Endpoints

| Método | Ruta | Auth | Descripción |
|---|---|---|---|
| GET | `/StartupAds/ClientScript` | anónimo | JS del frontend (recurso embebido). |
| GET | `/StartupAds/ClientStyle` | anónimo | CSS del overlay. |
| GET | `/StartupAds/Config` | usuario | Settings públicos + anuncios activos para el usuario. |
| GET | `/StartupAds/Media/{adId}` | usuario | Stream del archivo del anuncio (con *range*). |
| GET | `/StartupAds/Media/{adId}/Background` | usuario | Imagen de fondo opcional. |
| POST | `/StartupAds/Track/{adId}/{kind}` | usuario | Estadística (`shown`/`skipped`/`completed`/`clicked`). |
| GET/POST | `/StartupAds/Admin/Configuration` | admin | Leer / guardar configuración general. |
| GET/POST | `/StartupAds/Admin/Advertisements` | admin | Listar / crear anuncios. |
| POST/DELETE | `/StartupAds/Admin/Advertisements/{id}` | admin | Actualizar / eliminar. |
| POST | `/StartupAds/Admin/Advertisements/{id}/Duplicate` | admin | Duplicar. |
| POST | `/StartupAds/Admin/Advertisements/{id}/Enabled/{bool}` | admin | Activar / desactivar. |
| POST | `/StartupAds/Admin/ValidatePath` | admin | Validar la ruta de anuncios. |
| GET | `/StartupAds/Admin/Files` | admin | Archivos compatibles en la ruta. |
| POST | `/StartupAds/Admin/Scan` | admin | Importar archivos como anuncios. |
| GET | `/StartupAds/Admin/Preview?adId=` | admin | Datos para la vista previa. |

### Almacenamiento

Se usa el sistema de configuración de plugins de Jellyfin
(`plugins/configurations/Jellyfin.Plugin.StartupAds.xml`). La lista de anuncios y
las estadísticas viven **dentro** de `PluginConfiguration` (listas serializadas),
de modo que todo persiste tras reiniciar y no hay ficheros de metadatos sueltos.

---

## 3. Compatibilidad por cliente

La compatibilidad depende de si el cliente usa **Jellyfin Web** (una webview con
`index.html`) o una UI nativa (no ejecuta nuestro `<script>`).

| Cliente | Compatibilidad | Nota |
|---|---|---|
| Jellyfin Web (navegador) | ✅ Completo | Objetivo principal. |
| Chrome / Firefox / Edge | ✅ Completo | |
| Jellyfin Media Player (desktop) | ✅ Completo | Embebe Jellyfin Web; misma `index.html`. |
| Android / iOS app (vistas WebView) | ⚠️ Parcial | Las apps móviles son mayormente nativas; el overlay aparece sólo donde se renderiza Jellyfin Web. Autoplay de vídeo con sonido casi siempre bloqueado. |
| Android TV / Fire TV | ❌ No | UI nativa, no ejecuta JS de Jellyfin Web. |
| Roku | ❌ No | App nativa BrightScript. |
| Kodi (add-on) | ❌ No | Cliente nativo. |
| Apple TV (Swiftfin) | ❌ No | Cliente nativo. |

> No se promete compatibilidad donde técnicamente no existe. Para clientes nativos
> la única vía sería que cada app implementara el sistema; queda fuera del alcance
> de un plugin de servidor.

**Autoplay de vídeo:** los navegadores exigen `muted` para autoplay sin
interacción. El plugin arranca en `autoplay + muted` y reintenta *muted* si el
navegador bloquea la reproducción. Que el vídeo tenga sonido automático **no** es
posible de forma fiable en ningún navegador.

---

## 4. Estructura del proyecto

```
anuncio/
├── Jellyfin.Plugin.StartupAds.sln
├── manifest.json                      # manifiesto de repositorio de plugins
├── build.yaml                         # metadatos para jprm
├── README.md  /  LICENSE  /  .gitignore
│
├── Jellyfin.Plugin.StartupAds/
│   ├── Jellyfin.Plugin.StartupAds.csproj
│   ├── Plugin.cs
│   ├── PluginServiceRegistrator.cs
│   ├── Configuration/
│   │   ├── PluginConfiguration.cs
│   │   ├── Advertisement.cs
│   │   └── configPage.html            # página del Dashboard (embebida)
│   ├── Api/
│   │   ├── StartupAdsController.cs
│   │   └── Dtos.cs
│   ├── Services/
│   │   ├── MediaFileService.cs
│   │   ├── AdvertisementManager.cs
│   │   └── ScriptInjectionHostedService.cs
│   └── Web/
│       ├── startup-ads.js             # frontend (embebido, servido por el backend)
│       └── startup-ads.css
│
└── Jellyfin.Plugin.StartupAds.Tests/
    ├── MediaFileServiceTests.cs        # path traversal, validación, enumeración
    └── SchedulingTests.cs             # fechas, días, franjas horarias
```

---

## 5. Plan de implementación

1. Backend: configuración + modelo + `MediaFileService` (seguridad de rutas). ✔
2. `AdvertisementManager`: CRUD, escaneo, selección con orden/prioridad/aleatorio/
   programación/segmentación/tope por inicio. ✔
3. API REST (usuario + admin) con *policies* de autorización. ✔
4. Inyección de `<script>` vía `IHostedService` idempotente y reversible. ✔
5. Frontend: bootstrap, overlay, contador, *skip* (2 modos), acción de botón,
   cleanup, i18n es/en, accesibilidad (ESC, foco, `aria`). ✔
6. Página del Dashboard: config general + tabla de anuncios + editor + validar
   ruta + escanear + vista previa. ✔
7. Tests de las partes críticas. ✔
8. Empaquetado (`manifest.json`, `build.yaml`) y documentación. ✔

---

## 6. Requisitos

- Jellyfin 10.10.x (Ubuntu Server u otro).
- .NET SDK 8.0 para compilar.
- Una carpeta en el servidor con imágenes/vídeos (p. ej. `/var/lib/jellyfin/ads`).

## 7. Compilación

```bash
cd anuncio
dotnet restore
dotnet build -c Release
# DLL resultante:
#   Jellyfin.Plugin.StartupAds/bin/Release/net8.0/Jellyfin.Plugin.StartupAds.dll
```

Tests:

```bash
dotnet test -c Release
```

Empaquetado opcional con [`jprm`](https://github.com/oddstr13/jellyfin-plugin-repository-manager):

```bash
pip install jprm
jprm plugin build .
```

## 8. Instalación

### Manual (Ubuntu)

```bash
sudo mkdir -p /var/lib/jellyfin/plugins/Jellyfin.Plugin.StartupAds_1.0.0.0
sudo cp Jellyfin.Plugin.StartupAds/bin/Release/net8.0/Jellyfin.Plugin.StartupAds.dll \
        /var/lib/jellyfin/plugins/Jellyfin.Plugin.StartupAds_1.0.0.0/
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/Jellyfin.Plugin.StartupAds_1.0.0.0
sudo systemctl restart jellyfin
```

Windows: `C:\ProgramData\Jellyfin\Server\plugins\Jellyfin.Plugin.StartupAds_1.0.0.0\`
(verifica la ruta real de datos de tu instalación).

### Vía catálogo

Sube el `.zip` y sirve `manifest.json` (actualiza `sourceUrl` y `checksum`).
Dashboard → Plugins → Repositorios → añade la URL del `manifest.json`.

### Carpeta de anuncios

```bash
sudo mkdir -p /var/lib/jellyfin/ads
sudo chown jellyfin:jellyfin /var/lib/jellyfin/ads
# copia aquí bienvenida.jpg, promocion.mp4, ...
```

Dashboard → Plugins → **Jellyfin Startup Ads** → *Ruta de anuncios* →
`/var/lib/jellyfin/ads` → **Validar ruta** → **Guardar**.

Tras instalar/actualizar el plugin **reinicia Jellyfin una vez** para que se
inyecte el `<script>` en `index.html`.

## 9. Configuración

Panel del Dashboard:

- **General**: activar, ruta, modo (Manual/Automático/Mixto), orden, frecuencia
  (una vez por sesión / cada inicio), modo de visualización (Modal / Pantalla
  completa / Banner central), duración por defecto, máx. anuncios por inicio,
  anuncio aleatorio único.
- **Contador / Omitir**: mostrar contador, permitir omitir, segundos para omitir,
  comportamiento del botón (deshabilitado durante la cuenta / aparece al terminar),
  botón X, cerrar con ESC.
- **Vídeo**: autoplay, silenciado, bucle, controles.
- **Apariencia**: opacidad del fondo, ancho/alto máximos, radio de bordes, color de
  acento, idioma (es/en).
- **Estadísticas**: opt‑in.

## 10. Creación de anuncios

Botón **Crear anuncio**. Campos: nombre interno, tipo (Imagen/Vídeo/Texto/
Multimedia), título, descripción (texto plano), archivo (desplegable con los
ficheros de la ruta), fondo opcional, ajuste (`contain`/`cover`), duración
(manual o *del vídeo*), prioridad, orden, estado, permitir omitir + segundos,
contador, botón (texto + acción: URL externa / item Jellyfin / cerrar),
programación (fecha inicio/fin, horas, días de la semana) y usuarios objetivo.

## 11. Anuncios de vídeo

`<video autoplay muted playsinline>`. Si *Duración = del vídeo*, se espera
`loadedmetadata` y el contador usa `video.duration`; `ended` cierra (o habilita
*Omitir*). Si el vídeo falla, se cae a la duración manual sin romper el overlay.

## 12. Cuenta regresiva y botón Omitir

- **Modo A – deshabilitado durante la cuenta**: el botón se ve como
  `Omitir en N` y se activa (`Omitir`) al llegar a `SkipAfterSeconds`.
- **Modo B – aparece al terminar**: el botón está oculto hasta `SkipAfterSeconds`.
- Si `AllowSkip = false`, el overlay se cierra solo al acabar la duración.
- Al pulsar Omitir / X / ESC: `cleanup()` inmediato — `video.pause()`, se libera
  el `src`, se limpian `setInterval`/`setTimeout` y todos los listeners, y el nodo
  se elimina del DOM tras la animación de salida.

## 13. Programación

Cada anuncio: `StartDate`/`EndDate` (fuera de rango → no se muestra),
`DaysOfWeek` (vacío = todos), `StartTime`/`EndTime` (`HH:mm`, hora local del
servidor). Toda la lógica está en `AdvertisementManager.IsWithinSchedule` y
cubierta por tests.

## 14. Solución de problemas

| Síntoma | Causa / solución |
|---|---|
| No aparece nada | ¿*Activar* on? ¿*Mostrar al iniciar* on? ¿Reiniciaste Jellyfin tras instalar? ¿La ruta valida OK? |
| "La ruta configurada no existe" | Crea la carpeta y da permisos al usuario `jellyfin`. |
| El anuncio no vuelve a salir | Frecuencia = *una vez por sesión*: cierra la pestaña o usa *cada inicio*. |
| El vídeo no arranca solo | El navegador bloquea autoplay con sonido: mantén *silenciado* activado. |
| Tras actualizar Jellyfin Web desaparece | Normal: la actualización regenera `index.html`. Reinicia Jellyfin y el plugin lo reinyecta. |
| Sale en el móvil a medias | Las apps móviles son nativas salvo algunas vistas web; comportamiento esperado. |

## 15. Limitaciones

1. Clientes nativos (Android TV, Roku, Kodi, Swiftfin) **no** soportados.
2. La inyección modifica `index.html`; una actualización de Jellyfin Web la borra
   (se reinyecta al reiniciar). No es una API oficial y podría cambiar en el futuro.
3. Autoplay de vídeo **con** sonido: no es posible de forma fiable.
4. La navegación a un item de Jellyfin usa `Dashboard.navigate` / hash routing; si
   el equipo de Jellyfin cambia el router, habría que ajustar `handleAction`.
5. Las estadísticas son contadores agregados en el XML de configuración; no hay
   panel de informes (sólo los números).

## 16. Desarrollo y actualización a nuevas versiones de Jellyfin

Cuando Jellyfin cambie de versión mayor:

1. Actualiza `Jellyfin.Controller` / `Jellyfin.Model` al nuevo número.
2. Ajusta `TargetFramework` si cambia el runtime (10.11 → `net9.0`).
3. Actualiza `targetAbi` en `manifest.json` y `build.yaml`.
4. Verifica que siguen existiendo: `IHasWebPages`, `IPluginServiceRegistrator`,
   `IServerApplicationPaths.WebPath`, las *policies* `DefaultAuthorization` /
   `RequiresElevation`, y el claim `Jellyfin-UserId`.
5. Revisa `web/index.html` (nombre y ubicación) y el router del cliente
   (`Dashboard.navigate`).
6. `dotnet test` y prueba manual con la checklist de la sección 17.

## 17. Pruebas manuales

1. **Aparición automática**: instala, reinicia, configura ruta, crea un anuncio de
   imagen, abre Jellyfin Web → debe salir el overlay sin tocar nada.
2. **Imagen / Vídeo / Texto / Multimedia**: un anuncio de cada tipo.
3. **Contador**: baja de N a 0; el botón cambia según el modo configurado.
4. **Omitir / X / ESC**: cierran al instante; el vídeo deja de sonar; Jellyfin
   queda usable.
5. **Botón de acción**: URL externa abre pestaña nueva; item Jellyfin navega a la
   ficha; "cerrar" cierra.
6. **Varios anuncios**: crea 3, `MaxAdsPerStartup=2` → se muestran 2 en cola.
7. **Aleatorio**: `RandomPick` on → uno distinto en cada carga.
8. **Programación**: `EndDate` en el pasado → no aparece.
9. **Segmentación**: asigna a un usuario → sólo ese usuario lo ve.
10. **Una vez por sesión**: navega → no reaparece; recarga pestaña nueva → sí.
11. **Sin ruta / ruta mala / sin anuncios**: Jellyfin funciona con normalidad.
12. **Vista previa** desde el Dashboard sin cerrar sesión.
13. `Admin/ValidatePath` con `/etc` → rechazado.

---

## Documentación técnica resumida

1. **Cómo funciona**: al arrancar el servidor, `ScriptInjectionHostedService`
   añade `<script src="StartupAds/ClientScript">` a `web/index.html`. Cada carga de
   Jellyfin Web ejecuta ese script una vez.
2. **Carga del frontend**: el `<script>` pide su código al backend (recurso
   embebido) → así el plugin controla su propio frontend sin tocar el sistema.
3. **Obtención de anuncios**: `GET /StartupAds/Config` → `AdvertisementManager
   .GetActiveForUser(userId, now)` filtra por modo de fuente, `Enabled`,
   `ShowOnStartup`, programación, segmentación y existencia del medio; ordena
   (prioridad/nombre/manual/aleatorio); aplica `RandomPick` y `MaxAdsPerStartup`.
4. **Validación de ruta**: `MediaFileService.ValidateDirectory` — absoluta, no
   directorio de sistema, existe, legible, con ficheros compatibles.
5. **Servido de medios**: `GET /StartupAds/Media/{adId}` → `ResolveFile` garantiza
   que el fichero está dentro de la carpeta configurada → `PhysicalFile` con
   soporte de *range*. El `<video>`/`<img>` añade `?api_key=` (token de sesión).
6. **Contador**: `setInterval` de 250 ms calcula `remaining` y `canSkip` a partir
   de `Date.now()`; un `setTimeout` marca el fin de la duración.
7. **Omitir**: `cleanup(reason)` — idempotente — para timers, pausa/libera el
   vídeo, quita listeners, anima la salida y elimina el nodo; luego procesa el
   siguiente anuncio de la cola.
8. **Sin duplicados**: guard `window.__startupAdsLoaded`; el `<script>` no se
   re‑ejecuta en navegación SPA; `sessionStorage` para "una vez por sesión";
   antes de crear el overlay se elimina cualquier `#startup-ads-overlay` previo.
9. **Clientes compatibles**: sólo los que renderizan Jellyfin Web (sección 3);
   se determina por si el cliente ejecuta `index.html`, no por suposición.
10. **Limitaciones**: sección 15.
11. **Actualización entre versiones**: sección 16.
