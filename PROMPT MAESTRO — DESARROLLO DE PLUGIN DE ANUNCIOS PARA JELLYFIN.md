# PROMPT MAESTRO — DESARROLLO DE PLUGIN DE ANUNCIOS PARA JELLYFIN

## 1. ROL

Actúa como un **arquitecto senior y desarrollador experto en Jellyfin Server, Jellyfin Web, C#, .NET, JavaScript, HTML5 y CSS**, especializado en desarrollo de plugins para Jellyfin.

Quiero que diseñes y desarrolles desde cero un plugin para Jellyfin llamado:

**Jellyfin Startup Ads**

El objetivo del plugin es agregar un sistema de **anuncios/publicidad/presentaciones multimedia que aparezcan automáticamente al abrir Jellyfin**.

No quiero simplemente un banner.

Quiero desarrollar un verdadero **sistema de anuncios multimedia para Jellyfin**, administrable desde el Dashboard del servidor.

---

# 2. IMPORTANTE: INVESTIGA PRIMERO

Antes de escribir código:

1. Analiza la arquitectura actual de Jellyfin.
2. Determina la versión estable/relevante actual de Jellyfin y su ABI.
3. Analiza cómo funcionan actualmente los plugins.
4. Analiza cómo otros plugins actuales modifican Jellyfin Web.
5. Analiza los mecanismos disponibles para inyectar JavaScript/CSS sin modificar directamente los archivos originales de Jellyfin.
6. Analiza File Transformation, JavaScript injection u otros mecanismos compatibles.
7. Analiza cómo crear páginas de configuración para plugins.
8. Analiza cómo almacenar la configuración del plugin.
9. Analiza cómo servir archivos multimedia desde el servidor Jellyfin de forma segura.
10. Analiza las limitaciones de los diferentes clientes Jellyfin.

NO asumas APIs antiguas.

Si una API cambió entre versiones, utiliza la API correspondiente a la versión objetivo.

Quiero que el proyecto quede preparado para Jellyfin 10.11.x como mínimo, salvo que tu investigación determine una versión objetivo más apropiada.

Explica primero las decisiones arquitectónicas antes de comenzar a generar el código.

---

# 3. OBJETIVO PRINCIPAL

Cuando un usuario abra Jellyfin, quiero que el plugin pueda mostrar automáticamente un anuncio antes de que el usuario continúe utilizando normalmente Jellyfin.

Conceptualmente:

Usuario abre Jellyfin

↓

Jellyfin carga

↓

El plugin detecta que la interfaz está disponible

↓

Se muestra el anuncio

↓

Comienza una cuenta regresiva

↓

El usuario puede:

- esperar a que termine
- cerrar/omitir el anuncio cuando corresponda
- hacer clic en un botón configurado
- reproducir un video
- visualizar una imagen
- visualizar contenido textual

↓

El anuncio desaparece

↓

El usuario continúa utilizando Jellyfin normalmente.

---

# 4. EL ANUNCIO NO DEBE SER UN SIMPLE BANNER

El sistema debe utilizar una interfaz tipo:

**Modal / Overlay / Fullscreen Advertisement**

Debe poder ocupar una parte importante de la pantalla o incluso toda la pantalla.

Ejemplo conceptual:

┌─────────────────────────────────────────────────────────────┐
│                                                             │
│                     ANUNCIO                                 │
│                                                             │
│             ┌─────────────────────────────┐                 │
│             │                             │                 │
│             │       IMAGEN / VIDEO        │                 │
│             │                             │                 │
│             │                             │                 │
│             └─────────────────────────────┘                 │
│                                                             │
│                  TÍTULO DEL ANUNCIO                         │
│                                                             │
│              Descripción personalizada                     │
│                                                             │
│                [ VER MÁS ]                                  │
│                                                             │
│                            Omitir en 05                      │
│                                                             │
└─────────────────────────────────────────────────────────────┘

La interfaz debe tener apariencia profesional, moderna y compatible con el diseño oscuro de Jellyfin.

---

# 5. TIPOS DE CONTENIDO

El sistema debe soportar como mínimo:

## A. IMAGEN

Formatos recomendados:

- JPG
- JPEG
- PNG
- WEBP
- GIF, si es técnicamente viable

Debe poder especificarse una imagen desde una ruta configurada.

Ejemplo:

/var/lib/jellyfin/ads/

---

# 6. VIDEO

Debe soportar anuncios en video.

Formatos recomendados:

- MP4
- WEBM
- otros formatos soportados nativamente por HTML5 si resulta viable.

El video debe utilizar:

```html
<video>
```

siempre que sea apropiado.

Debe contemplarse:

- autoplay
- muted
- loop opcional
- controles opcionales
- reproducción completa
- duración automática basada en el video
- duración configurada manualmente
- botón de omitir

IMPORTANTE:

Los navegadores normalmente requieren que los videos autoplay comiencen sin audio.

Por eso el sistema debe utilizar inicialmente:

```text
autoplay + muted
```

y permitir que el administrador configure el comportamiento.

---

# 7. TEXTO PERSONALIZADO

Debe existir un tipo de anuncio:

**Texto**

Ejemplo:

Título:

"¡Nuevo contenido disponible!"

Contenido:

"Ya están disponibles nuevas películas y series en nuestro servidor Jellyfin."

Botón:

"Continuar"

Debe permitir:

- título
- descripción
- texto enriquecido básico
- botón
- URL
- duración
- imagen de fondo opcional

Debe evitarse permitir HTML arbitrario inseguro.

Si se permite HTML/Markdown, debe sanitizarse correctamente.

---

# 8. ANUNCIO MULTIMEDIA

Idealmente un anuncio puede combinar:

- imagen
- video
- título
- descripción
- botón
- enlace
- contador

Ejemplo:

VIDEO

+

"NUEVO ESTRENO"

+

"Disponible ahora"

+

[ VER AHORA ]

---

# 9. RUTA DE LOS ANUNCIOS

Esta es una característica MUY IMPORTANTE.

NO quiero que las imágenes/videos tengan que estar obligatoriamente dentro de la carpeta del plugin.

Quiero que el administrador pueda configurar una ruta externa.

Ejemplo:

```text
/var/lib/jellyfin/ads
```

o:

```text
/mnt/media/anuncios
```

o:

```text
/home/jellyfin/publicidad
```

En Windows debería ser posible algo como:

```text
C:\Jellyfin\Ads
```

La ruta debe aparecer en:

**Dashboard → Plugins → Jellyfin Startup Ads → Configuración**

Campo:

```text
Ruta de anuncios
```

Ejemplo:

```text
/var/lib/jellyfin/ads
```

Debe existir un botón:

**Guardar**

Y preferiblemente:

**Validar ruta**

El plugin debe comprobar:

- que la ruta existe
- que Jellyfin tiene permisos de lectura
- que contiene archivos compatibles
- que no apunta a una ruta peligrosa

---

# 10. SEGURIDAD DE RUTAS

NO permitas que una URL del navegador pueda acceder arbitrariamente a cualquier archivo del sistema.

No quiero algo como:

```text
/ads?path=/etc/passwd
```

El backend debe controlar estrictamente los archivos disponibles.

La aplicación debe:

1. Leer la ruta configurada.
2. Enumerar únicamente archivos permitidos.
3. Crear una lista segura de anuncios.
4. Servir únicamente archivos dentro de la ruta configurada.
5. Evitar path traversal.
6. Evitar:

```text
../
```

7. Normalizar las rutas.
8. Validar extensiones.
9. Evitar acceso a archivos fuera del directorio configurado.

---

# 11. ADMINISTRACIÓN DESDE JELLYFIN

Quiero una página de configuración del plugin dentro de:

**Dashboard → Plugins → Jellyfin Startup Ads**

La configuración debe ser profesional.

Debe contener como mínimo:

## CONFIGURACIÓN GENERAL

- Activar/desactivar anuncios
- Mostrar anuncios al iniciar
- Ruta de anuncios
- Duración predeterminada
- Mostrar botón Omitir
- Tiempo antes de permitir Omitir
- Mostrar contador
- Reproducir automáticamente
- Reproducir video sin sonido
- Repetir video

---

# 12. GESTIÓN DE ANUNCIOS

No quiero depender exclusivamente de detectar archivos automáticamente.

Quiero que el plugin pueda administrar anuncios individualmente.

Ejemplo:

```text
ANUNCIOS

-------------------------------------------------------
Nombre             Tipo       Estado       Prioridad
-------------------------------------------------------
Nuevo contenido    Video      Activo       10
Mantenimiento      Imagen     Activo        5
Bienvenida         Texto      Activo        1
-------------------------------------------------------
```

Botones:

- Crear
- Editar
- Eliminar
- Activar
- Desactivar
- Duplicar
- Vista previa

---

# 13. CREAR ANUNCIO

Debe existir un formulario:

## Información

Nombre interno:

```text
Nuevo estreno septiembre
```

Tipo:

```text
Imagen
Video
Texto
Multimedia
```

Título:

```text
¡Nuevo contenido!
```

Descripción:

```text
Hemos agregado nuevas películas.
```

---

# 14. ARCHIVO DEL ANUNCIO

Debe poder seleccionar un archivo existente dentro de la ruta configurada.

Ejemplo:

```text
Ruta configurada:

/var/lib/jellyfin/ads/

Archivos:

nuevo-contenido.jpg
mantenimiento.mp4
bienvenida.png
promo-septiembre.mp4
```

El administrador selecciona:

```text
promo-septiembre.mp4
```

No quiero que el navegador pueda proporcionar arbitrariamente:

```text
/etc/archivo
```

---

# 15. BOTÓN DEL ANUNCIO

Debe permitir configurar:

Texto:

```text
Ver ahora
```

Acción:

- URL externa
- URL interna
- ruta Jellyfin
- reproducir contenido
- cerrar anuncio

Ejemplo:

```text
https://miweb.com/promocion
```

También debe ser posible no mostrar ningún botón.

---

# 16. CUENTA REGRESIVA

Este requisito es obligatorio.

Cuando aparece el anuncio:

```text
Omitir en 10
```

Luego:

```text
Omitir en 9
```

```text
Omitir en 8
```

...

```text
Omitir en 1
```

Cuando llegue a:

```text
0
```

debe convertirse en:

```text
OMITIR
```

o aparecer un botón:

```text
[ OMITIR ]
```

El administrador debe poder configurar:

```text
Duración del anuncio: 10 segundos

Permitir omitir después de: 5 segundos
```

Por ejemplo:

```text
5 segundos restantes

[ Omitir ]
```

Quiero que la implementación soporte ambas configuraciones:

### Opción A

El botón está deshabilitado mientras el contador es mayor que 0.

### Opción B

El botón aparece solamente cuando llega a 0.

Debe poder configurarse.

---

# 17. COMPORTAMIENTO DEL BOTÓN OMITIR

Al presionar:

```text
OMITIR
```

el anuncio debe desaparecer inmediatamente.

Debe detener:

- video
- audio
- temporizadores
- eventos
- listeners

Debe liberar correctamente los recursos.

No debe dejar elementos ocultos bloqueando Jellyfin.

---

# 18. DURACIÓN

Debe existir:

```text
Duración del anuncio
```

Ejemplo:

```text
10 segundos
```

Pero para videos quiero soportar:

```text
Usar duración del video
```

Por ejemplo:

```text
Duración:

○ Configurada manualmente

○ Duración del video
```

Si se utiliza la duración del video:

- detectar duración
- iniciar contador
- finalizar cuando termine
- permitir omitir si está habilitado

---

# 19. MODO AUTOMÁTICO

Quiero que el plugin pueda detectar automáticamente los archivos existentes en la carpeta configurada.

Ejemplo:

```text
/var/lib/jellyfin/ads/
```

Contiene:

```text
01-bienvenida.jpg
02-promocion.mp4
03-mantenimiento.jpg
04-nuevo-contenido.mp4
```

El plugin debe poder crear automáticamente anuncios a partir de esos archivos.

Pero también debe existir una configuración manual.

Por ejemplo:

```text
Modo de anuncios:

○ Manual
○ Automático
○ Mixto
```

---

# 20. ORDEN DE LOS ANUNCIOS

Debe soportarse:

- prioridad
- orden manual
- orden aleatorio

Ejemplo:

```text
Orden:

○ Prioridad
○ Nombre
○ Aleatorio
○ Orden manual
```

---

# 21. VARIOS ANUNCIOS

Debe ser posible tener varios anuncios.

Ejemplo:

```text
Usuario abre Jellyfin

↓

Anuncio 1

↓

Finaliza

↓

Anuncio 2

↓

Finaliza

↓

Jellyfin
```

Pero también quiero configurar:

```text
Máximo de anuncios por inicio:

1
```

o:

```text
3
```

---

# 22. MOSTRAR UNO ALEATORIO

Debe existir:

```text
Mostrar anuncio aleatorio
```

Si existen:

```text
10 anuncios activos
```

el sistema puede seleccionar uno aleatoriamente.

---

# 23. PROGRAMACIÓN

Quiero que cada anuncio pueda tener:

```text
Fecha de inicio
Fecha de finalización
```

Ejemplo:

```text
Inicio:
01/09/2026

Fin:
30/09/2026
```

Antes del inicio:

```text
NO mostrar
```

Después de la fecha final:

```text
NO mostrar
```

También sería ideal soportar:

```text
Días de la semana
Hora de inicio
Hora de finalización
```

si esto puede implementarse limpiamente.

---

# 24. MOSTRAR UNA VEZ POR SESIÓN

Debe existir una opción:

```text
Mostrar una vez por sesión
```

Ejemplo:

Usuario abre Jellyfin:

→ aparece anuncio.

Usuario navega:

→ no vuelve a aparecer.

Usuario cierra Jellyfin.

Usuario vuelve a abrir:

→ aparece nuevamente.

Debe definir claramente qué significa "sesión".

---

# 25. MOSTRAR SIEMPRE

También:

```text
Mostrar en cada inicio
```

Cada vez que Jellyfin Web se inicialice:

→ mostrar anuncio.

---

# 26. USUARIOS

Idealmente permitir:

```text
Todos los usuarios
```

o:

```text
Usuarios específicos
```

Ejemplo:

```text
☑ Francisco
☑ Juan
☐ Pedro
```

Si es viable utilizando las APIs oficiales de Jellyfin.

---

# 27. DISPOSITIVOS

Investiga qué clientes pueden soportar correctamente este sistema.

Clasifica como mínimo:

- Jellyfin Web
- navegador Chrome
- Firefox
- Edge
- Jellyfin Media Player
- Android
- iOS
- Android TV
- Fire TV
- Roku
- otros clientes

NO prometas compatibilidad donde técnicamente no sea posible.

Documenta claramente:

```text
Cliente             Compatibilidad
------------------------------------
Jellyfin Web         ✓
Chrome               ✓
Firefox              ✓
Jellyfin MediaPlayer ✓
Android WebView      ?
Android TV           ?
Roku                 ?
```

La compatibilidad debe determinarse técnicamente, no asumirse.

---

# 28. DISEÑO VISUAL

Quiero una interfaz moderna.

Debe integrarse visualmente con Jellyfin.

Preferencias:

- Dark mode
- responsive
- bordes redondeados
- sombras suaves
- overlay oscuro
- animaciones suaves
- botón de cerrar
- contador visible
- buena legibilidad
- excelente comportamiento en pantallas grandes

No quiero una ventana antigua o con apariencia genérica.

---

# 29. ANIMACIONES

Agregar animaciones suaves:

Entrada:

```text
fade-in
```

Salida:

```text
fade-out
```

Opcional:

```text
scale-in
```

No usar animaciones pesadas que afecten el rendimiento.

---

# 30. RESPONSIVE

Debe funcionar correctamente en:

- 1920×1080
- 1366×768
- 1280×720
- tablets
- teléfonos

El video o imagen nunca debe deformarse.

Utilizar:

```text
object-fit: contain
```

o:

```text
object-fit: cover
```

según el modo configurado.

---

# 31. MODOS DE VISUALIZACIÓN

Si es viable, agregar:

```text
Modo:

○ Modal
○ Pantalla completa
○ Banner central
```

El modo predeterminado debe ser:

```text
Modal / Overlay
```

---

# 32. CONFIGURACIÓN DE APARIENCIA

Permitir configurar:

- ancho máximo
- alto máximo
- opacidad del overlay
- posición
- radio de bordes
- mostrar botón cerrar
- mostrar título
- mostrar descripción
- mostrar contador
- mostrar botón
- tamaño del botón

No compliques innecesariamente el sistema.

Los valores predeterminados deben verse bien sin configuración adicional.

---

# 33. API DEL PLUGIN

Quiero una arquitectura limpia.

Considera endpoints internos como:

```text
/api/StartupAds/config
/api/StartupAds/ads
/api/StartupAds/media/{id}
/api/StartupAds/active
```

Los nombres finales deben adaptarse a las convenciones reales de Jellyfin.

La API debe:

- validar autenticación
- respetar permisos
- validar rutas
- no exponer archivos arbitrarios
- devolver solamente anuncios permitidos

---

# 34. BACKEND

Utilizar:

```text
C#
.NET
Jellyfin Plugin API
```

Seguir las convenciones actuales de Jellyfin.

Separar responsabilidades.

Por ejemplo:

```text
Plugin.cs

PluginConfiguration.cs

StartupAdsService.cs

StartupAdsController.cs

StartupAdsManager.cs

Models/
    Advertisement.cs
    AdvertisementType.cs
    AdvertisementSettings.cs

Services/
    MediaScanner.cs
    AdvertisementSelector.cs
    AdvertisementFileService.cs

Web/
    startup-ads.js
    startup-ads.css
```

La estructura final puede cambiar si encuentras una arquitectura mejor.

---

# 35. FRONTEND

La parte frontend debe estar diseñada específicamente para integrarse con Jellyfin Web.

Utilizar JavaScript moderno sin introducir frameworks innecesarios.

Debe:

1. detectar la inicialización de Jellyfin Web
2. comprobar si el usuario está autenticado cuando corresponda
3. consultar los anuncios activos
4. seleccionar el anuncio
5. crear el overlay
6. reproducir el contenido
7. iniciar el contador
8. gestionar el botón Omitir
9. gestionar el botón de acción
10. cerrar limpiamente
11. evitar duplicados

---

# 36. EVITAR DUPLICADOS

Este requisito es MUY importante.

Jellyfin Web puede cambiar de vista sin recargar toda la aplicación.

El plugin NO debe mostrar:

```text
Anuncio 1
Anuncio 1
Anuncio 1
Anuncio 1
```

por múltiples inicializaciones del código.

Implementar mecanismos como:

```text
singleton
flag global
event listener cleanup
MutationObserver controlado
```

solo cuando sean realmente necesarios.

---

# 37. NO MODIFICAR DIRECTAMENTE JELLYFIN WEB

NO quiero modificar manualmente:

```text
/usr/share/jellyfin/web/
```

ni reemplazar archivos originales.

La solución debe utilizar mecanismos compatibles con plugins.

Investiga y selecciona el mecanismo correcto para la versión objetivo.

Si File Transformation es el mecanismo recomendado, implementarlo correctamente.

Si existe un mecanismo mejor, utilizarlo.

---

# 38. INSTALACIÓN

El plugin debe poder instalarse como plugin normal de Jellyfin.

Quiero:

```text
manifest.json
```

y estructura compatible con el catálogo/repositorio de plugins.

Debe incluir:

- nombre
- GUID
- versión
- descripción
- changelog
- target ABI
- DLL
- URL del repositorio
- icono si corresponde

---

# 39. BUILD

Quiero instrucciones completas para compilar.

Por ejemplo:

```bash
dotnet restore
dotnet build
```

Pero utiliza los comandos correctos según la estructura final.

Debe explicar:

- SDK necesario
- runtime
- versión .NET
- dependencias
- cómo generar DLL
- dónde colocar la DLL

---

# 40. INSTALACIÓN MANUAL

Documentar:

Linux:

```text
/var/lib/jellyfin/plugins/JellyfinStartupAds/
```

Windows:

```text
C:\ProgramData\Jellyfin\Server\plugins\JellyfinStartupAds\
```

Pero NO asumas estas rutas sin verificar las convenciones actuales.

Documenta las rutas correctas.

---

# 41. CONFIGURACIÓN INICIAL

Después de instalar:

```text
Dashboard
→ Plugins
→ Jellyfin Startup Ads
→ Configuración
```

Debe aparecer algo como:

```text
┌──────────────────────────────────────────────┐
│ Jellyfin Startup Ads                        │
├──────────────────────────────────────────────┤
│                                              │
│ ☑ Activar anuncios                          │
│                                              │
│ Ruta de anuncios:                           │
│ [/var/lib/jellyfin/ads_____________]        │
│                                              │
│ [ Validar ruta ]                             │
│                                              │
│ Duración predeterminada: [10] segundos      │
│                                              │
│ ☑ Mostrar contador                           │
│ ☑ Permitir omitir                            │
│                                              │
│ Modo:                                        │
│ [ Modal ▼ ]                                  │
│                                              │
│                [ GUARDAR ]                   │
└──────────────────────────────────────────────┘
```

---

# 42. PREVISUALIZACIÓN

Quiero un botón:

```text
Vista previa
```

El administrador debe poder ver cómo aparecerá el anuncio sin cerrar sesión.

Idealmente:

```text
Dashboard
→ Plugin
→ Anuncio
→ Vista previa
```

---

# 43. PRUEBA

Debe existir:

```text
Mostrar anuncio de prueba
```

para verificar:

- imagen
- video
- contador
- botón
- cierre
- enlace

---

# 44. LOGGING

Implementar logs útiles.

Ejemplos:

```text
[Jellyfin Startup Ads] Plugin initialized.

[Jellyfin Startup Ads] Ads directory:
 /var/lib/jellyfin/ads

[Jellyfin Startup Ads] Found 5 media files.

[Jellyfin Startup Ads] Selected advertisement:
 nuevo-contenido.mp4

[Jellyfin Startup Ads] Advertisement displayed.

[Jellyfin Startup Ads] Advertisement dismissed.
```

No registrar información sensible.

---

# 45. MANEJO DE ERRORES

Si:

```text
la ruta no existe
```

no debe romper Jellyfin.

Mostrar:

```text
La ruta configurada no existe.
```

Si:

```text
no hay anuncios
```

simplemente:

```text
no mostrar nada
```

Si:

```text
el video no puede reproducirse
```

intentar manejar el error limpiamente.

Nunca bloquear el acceso normal a Jellyfin debido a un error del plugin.

---

# 46. FALLBACK

Este requisito es obligatorio.

Si el plugin falla:

```text
Jellyfin debe continuar funcionando normalmente.
```

El anuncio nunca debe convertirse en un punto único de fallo.

---

# 47. RENDIMIENTO

El plugin debe ser ligero.

No quiero:

- polling cada 100 ms
- loops infinitos
- múltiples MutationObserver innecesarios
- consumo elevado de CPU
- fugas de memoria
- listeners duplicados

Utilizar:

- eventos
- timers controlados
- cleanup
- lazy loading
- carga solamente cuando sea necesario

Los videos no deben descargarse hasta que realmente se vaya a mostrar el anuncio.

---

# 48. CACHE

Considerar cache de:

- lista de anuncios
- metadata
- configuración

pero sin impedir que el administrador pueda actualizar un anuncio.

---

# 49. CONFIGURACIÓN EN XML

Utilizar el sistema de configuración de plugins de Jellyfin.

La configuración debe persistir después de reiniciar Jellyfin.

Ejemplo conceptual:

```csharp
public class PluginConfiguration
{
    public bool Enabled { get; set; }

    public string AdsDirectory { get; set; }

    public int DefaultDurationSeconds { get; set; }

    public bool ShowOnStartup { get; set; }

    public bool ShowCountdown { get; set; }

    public bool AllowSkip { get; set; }

    public int SkipAfterSeconds { get; set; }
}
```

Pero adapta la implementación a las APIs reales.

---

# 50. MODELO DE ANUNCIO

Crear un modelo robusto.

Ejemplo conceptual:

```text
Advertisement

Id
Name
Type
Title
Description
MediaFile
Duration
UseMediaDuration
Priority
Enabled
StartDate
EndDate
ShowOnStartup
AllowSkip
SkipAfterSeconds
ShowCountdown
ButtonText
ButtonAction
ButtonUrl
BackgroundMode
Order
```

Puedes modificarlo si la arquitectura final lo requiere.

---

# 51. FORMATO DE DATOS

No dependas solamente de nombres de archivos.

Quiero metadata propia para cada anuncio.

Puedes utilizar:

```text
XML
JSON
```

o el sistema de configuración de Jellyfin.

Selecciona la opción más apropiada.

---

# 52. EXPERIENCIA DEL USUARIO

El usuario NO debe tener que hacer nada.

Ejemplo:

Usuario:

```text
Abre Jellyfin
```

Resultado:

```text
┌─────────────────────────────────────┐
│                                     │
│        NUEVO CONTENIDO              │
│                                     │
│          [ VIDEO ]                  │
│                                     │
│    Mira las novedades               │
│                                     │
│        Omitir en 5                  │
│                                     │
└─────────────────────────────────────┘
```

Después:

```text
Omitir en 0
```

se convierte en:

```text
[ OMITIR ]
```

Usuario pulsa:

```text
OMITIR
```

Resultado:

```text
Jellyfin normal
```

---

# 53. BOTÓN DE ACCIÓN

Ejemplo:

```text
[ VER AHORA ]
```

Debe poder abrir:

```text
URL externa
```

o:

```text
contenido dentro de Jellyfin
```

Quiero investigar cuál es la forma correcta de navegar internamente dentro de Jellyfin Web sin romper el estado de la aplicación.

---

# 54. SOPORTE PARA CONTENIDO JELLYFIN

Si es técnicamente posible, permitir configurar un anuncio cuyo botón lleve directamente a:

- película
- serie
- episodio
- colección

utilizando los identificadores internos de Jellyfin.

Ejemplo conceptual:

```text
MovieId:
xxxxxxxxxxxxxxxx
```

Al pulsar:

```text
[ VER AHORA ]
```

abrir la página correspondiente.

No inventes rutas de navegación si Jellyfin tiene un mecanismo oficial o más compatible.

Investígalo.

---

# 55. ANALÍTICAS OPCIONALES

Si es viable, registrar:

```text
veces mostrado
veces omitido
veces completado
clics
```

Pero esto debe ser opcional.

Configuración:

```text
☑ Activar estadísticas
```

No almacenar datos personales innecesarios.

---

# 56. ADMINISTRADOR

Solo usuarios con permisos administrativos deben poder:

- modificar configuración
- crear anuncios
- eliminar anuncios
- cambiar rutas
- activar/desactivar anuncios

Los usuarios normales solamente deben recibir el anuncio.

---

# 57. INTERNACIONALIZACIÓN

Preparar el frontend para español e inglés.

Por defecto:

```text
español
```

Textos:

```text
Omitir en
Omitir
Cerrar
Ver ahora
```

No hardcodear todos los textos si Jellyfin proporciona mecanismos adecuados de localización.

---

# 58. ACCESIBILIDAD

Considerar:

- teclado
- ESC para cerrar si está permitido
- aria-label
- contraste
- focus management
- lectores de pantalla

No permitir que el modal cree problemas graves de accesibilidad.

---

# 59. SEGURIDAD FRONTEND

No confiar en valores provenientes del servidor.

Sanitizar:

- título
- descripción
- URL
- nombres de archivos

Evitar XSS.

No utilizar:

```javascript
innerHTML
```

con contenido no confiable sin sanitización.

---

# 60. ESTRUCTURA DEL PROYECTO

Quiero que propongas una estructura profesional.

Ejemplo:

```text
Jellyfin.Plugin.StartupAds/
│
├── Jellyfin.Plugin.StartupAds.csproj
├── Plugin.cs
├── PluginConfiguration.cs
├── manifest.json
├── README.md
├── LICENSE
│
├── Controllers/
│   └── StartupAdsController.cs
│
├── Models/
│   ├── Advertisement.cs
│   ├── AdvertisementType.cs
│   └── AdvertisementSettings.cs
│
├── Services/
│   ├── AdvertisementService.cs
│   ├── AdvertisementScanner.cs
│   ├── AdvertisementSelector.cs
│   └── MediaFileService.cs
│
├── Configuration/
│   └── ConfigurationPage.html
│
├── Web/
│   ├── startup-ads.js
│   └── startup-ads.css
│
└── Properties/
```

Puedes modificar esta estructura si la arquitectura real de Jellyfin recomienda otra.

---

# 61. TESTS

Quiero tests para las partes críticas.

Como mínimo:

- selección de anuncios
- validación de rutas
- prevención de path traversal
- fechas de activación
- selección aleatoria
- duración
- configuración
- permisos

---

# 62. DOCUMENTACIÓN

Genera un README profesional.

Debe incluir:

## Descripción

## Características

## Requisitos

## Compatibilidad

## Instalación

## Configuración

## Creación de anuncios

## Configuración de rutas

## Anuncios de video

## Cuenta regresiva

## Botón Omitir

## Programación

## Solución de problemas

## Limitaciones

## Desarrollo

## Compilación

## Arquitectura

---

# 63. NO QUIERO UNA IMPLEMENTACIÓN DE EJEMPLO

Quiero un proyecto funcional.

No quiero respuestas del tipo:

```text
Aquí tienes un ejemplo de cómo hacerlo.
```

Quiero:

```text
código completo
```

que pueda compilarse.

No omitas archivos importantes.

No pongas:

```text
// resto del código...
```

No uses:

```text
TODO
```

para funcionalidades principales.

No sustituyas partes importantes por pseudocódigo.

---

# 64. SI EXISTE UNA LIMITACIÓN TÉCNICA

Si descubres que alguna característica no puede implementarse exactamente como la solicito:

1. Explícala.
2. Explica por qué.
3. Propón la alternativa más cercana.
4. No inventes una API.
5. No finjas compatibilidad.

Por ejemplo, si Jellyfin Web permite una característica pero Android TV no, documentarlo claramente.

---

# 65. PRIORIDAD DE COMPATIBILIDAD

La prioridad es:

1. Jellyfin Web
2. Chrome
3. Firefox
4. Edge
5. Jellyfin Media Player
6. Android/iOS basados en WebView
7. Otros clientes si técnicamente son compatibles

NO asumir compatibilidad universal.

---

# 66. DISEÑO ARQUITECTÓNICO

Antes de escribir código quiero que me entregues:

### Fase 1 — Análisis

- versión Jellyfin objetivo
- ABI
- .NET
- mecanismo de inyección
- arquitectura
- limitaciones
- compatibilidad

### Fase 2 — Diseño

Diagrama:

```text
Jellyfin Server
       │
       ├── Plugin Backend
       │       │
       │       ├── Configuration
       │       ├── Advertisement Manager
       │       ├── Media Scanner
       │       └── API
       │
       └── Jellyfin Web
               │
               └── Startup Ads UI
                       │
                       ├── Image
                       ├── Video
                       ├── Text
                       ├── Countdown
                       └── Skip
```

### Fase 3 — Código

Generar todos los archivos.

### Fase 4 — Compilación

Explicar cómo compilar.

### Fase 5 — Instalación

Explicar cómo instalar en Ubuntu.

### Fase 6 — Pruebas

Explicar cómo probar cada funcionalidad.

---

# 67. ENTORNO DE PRODUCCIÓN

Mi servidor Jellyfin está funcionando sobre:

```text
Ubuntu Server
```

Por lo tanto, las instrucciones deben priorizar Linux/Ubuntu.

Pero el plugin debe intentar mantenerse multiplataforma.

No asumir Docker si no es necesario.

---

# 68. RUTA DE PRODUCCIÓN

Quiero poder configurar algo como:

```text
/var/lib/jellyfin/ads
```

y colocar:

```text
/var/lib/jellyfin/ads/
├── bienvenida.jpg
├── promocion.mp4
├── mantenimiento.jpg
└── nuevo-contenido.mp4
```

Después de configurar la ruta:

```text
Dashboard
→ Plugins
→ Jellyfin Startup Ads
→ Ruta de anuncios
→ /var/lib/jellyfin/ads
→ Guardar
```

el plugin debe poder utilizar esos archivos.

---

# 69. REQUISITO CRÍTICO SOBRE LA RUTA

NO quiero que la ruta quede fija en el código.

MAL:

```csharp
var path = "/var/lib/jellyfin/ads";
```

BIEN:

```text
PluginConfiguration.AdsDirectory
```

La ruta debe ser totalmente configurable desde el Dashboard.

---

# 70. REQUISITO CRÍTICO SOBRE EL INICIO

El anuncio debe aparecer cuando se abra/inicialice Jellyfin Web.

No quiero que el usuario tenga que:

```text
ir al Dashboard
```

ni:

```text
abrir una sección
```

ni:

```text
hacer clic en un botón
```

Debe suceder automáticamente.

---

# 71. NO BLOQUEAR JELLYFIN

El anuncio puede estar delante de la interfaz, pero no debe romper Jellyfin.

Si el usuario cierra el anuncio:

```text
Jellyfin funciona normalmente.
```

Si no existe anuncio:

```text
Jellyfin funciona normalmente.
```

Si hay error:

```text
Jellyfin funciona normalmente.
```

---

# 72. DETECCIÓN DE ARRANQUE

Investiga cuidadosamente cuál es la forma más robusta de detectar:

```text
Jellyfin Web listo
```

No dependas únicamente de:

```javascript
window.onload
```

si Jellyfin utiliza navegación SPA.

Debes entender cómo funciona el ciclo de vida de Jellyfin Web.

---

# 73. SPA

Jellyfin Web utiliza navegación dinámica.

El plugin debe evitar mostrar anuncios múltiples veces debido a:

- navegación
- cambio de usuario
- cambio de página
- renderizado de componentes
- recarga parcial

Diseña una estrategia robusta.

---

# 74. SESIÓN

Diseña una estrategia para:

```text
show once per session
```

Puede utilizar:

```text
sessionStorage
```

si resulta apropiado.

Pero analiza primero las implicaciones.

---

# 75. CAMBIO DE USUARIO

Si Jellyfin permite cambiar de usuario sin recargar completamente la página:

```text
Usuario A
↓
Usuario B
```

el sistema debe reevaluar correctamente los anuncios permitidos.

---

# 76. VIDEO

Cuando el usuario omita:

```text
video.pause()
```

y liberar correctamente el elemento.

Al cerrar:

```text
video.src = ''
```

o la estrategia apropiada.

Evitar consumo de memoria.

---

# 77. MODAL

El modal debe:

- tener z-index correcto
- quedar por encima de Jellyfin
- bloquear interacción mientras está activo
- permitir cierre si está habilitado
- no modificar permanentemente la estructura de Jellyfin

---

# 78. BOTÓN CERRAR

Además del botón:

```text
OMITIR
```

puede existir:

```text
X
```

pero debe ser configurable.

Opciones:

```text
☑ Mostrar botón X
☑ Permitir cerrar con ESC
```

---

# 79. CONFIGURACIÓN RECOMENDADA POR DEFECTO

Usa valores razonables:

```text
Enabled = true

ShowOnStartup = true

DefaultDuration = 10 segundos

ShowCountdown = true

AllowSkip = true

SkipAfter = 5 segundos

AutoplayVideo = true

MutedVideo = true

LoopVideo = false

ShowCloseButton = false
```

Ajusta estos valores si tu análisis técnico determina mejores defaults.

---

# 80. RESULTADO ESPERADO

Al finalizar quiero tener:

```text
Jellyfin
   │
   └── Plugin
       │
       ├── Dashboard Configuration
       │
       ├── Advertisement Manager
       │
       ├── Media Directory
       │
       ├── Image Support
       │
       ├── Video Support
       │
       ├── Text Support
       │
       ├── Countdown
       │
       ├── Skip Button
       │
       ├── Scheduling
       │
       ├── User Targeting
       │
       ├── Statistics
       │
       └── Startup Overlay
```

---

# 81. MUY IMPORTANTE — ANTES DE ENTREGAR

Antes de considerar terminado el proyecto, realiza una revisión final:

### Backend

- [ ] Compila
- [ ] Configuración funciona
- [ ] Ruta configurable
- [ ] Seguridad de archivos
- [ ] API protegida
- [ ] Logs
- [ ] Manejo de errores

### Frontend

- [ ] Carga automáticamente
- [ ] No duplica anuncios
- [ ] Modal funciona
- [ ] Imagen funciona
- [ ] Video funciona
- [ ] Texto funciona
- [ ] Contador funciona
- [ ] Omitir funciona
- [ ] Botón funciona
- [ ] Responsive funciona
- [ ] Cleanup funciona

### Jellyfin

- [ ] Plugin aparece en Dashboard
- [ ] Configuración persiste
- [ ] Jellyfin continúa funcionando sin anuncios
- [ ] Jellyfin continúa funcionando si hay errores
- [ ] No modifica permanentemente archivos originales
- [ ] Documenta mecanismo de inyección
- [ ] Documenta compatibilidad por cliente

---

# 82. FORMATO DE TU RESPUESTA

Quiero que trabajes por fases.

NO generes inmediatamente cientos de líneas de código sin explicar la arquitectura.

Primero entrega:

## 1. Análisis técnico

## 2. Arquitectura propuesta

## 3. Compatibilidad con Jellyfin

## 4. Estructura del proyecto

## 5. Plan de implementación

Después, si la arquitectura es correcta, genera:

## 6. Código completo

## 7. Manifest

## 8. Configuración

## 9. Frontend

## 10. Backend

## 11. Instalación

## 12. Compilación

## 13. Pruebas

## 14. README

---

# 83. REGLA FINAL

Quiero que pienses como si este plugin fuera a instalarse en un servidor Jellyfin de producción con múltiples usuarios.

No construyas un simple prototipo visual.

Construye una arquitectura:

- segura
- mantenible
- modular
- eficiente
- extensible
- compatible con Jellyfin
- preparada para futuras versiones
- con buen manejo de errores
- con buena experiencia de usuario

El objetivo final es tener un verdadero:

**"Jellyfin Startup Advertisement / Announcement System"**

que permita mostrar automáticamente anuncios multimedia al iniciar Jellyfin.

Cuando termines la implementación completa, prepara también una documentación técnica donde expliques exactamente:

1. Cómo funciona.
2. Cómo se carga el frontend.
3. Cómo el backend obtiene los anuncios.
4. Cómo se valida la ruta.
5. Cómo se sirve el contenido multimedia.
6. Cómo funciona el contador.
7. Cómo funciona Omitir.
8. Cómo se evita mostrar anuncios duplicados.
9. Cómo se determina qué clientes son compatibles.
10. Qué limitaciones tiene la implementación.
11. Cómo actualizar el plugin cuando Jellyfin cambie de versión.