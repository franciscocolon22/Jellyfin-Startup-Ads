# Jellyfin Startup Ads

Plugin para **Jellyfin Server 10.11.11** con **dos sistemas de anuncios independientes**:

1. **Anuncios de la presentación** — overlay multimedia (imagen / vídeo / texto) al abrir
   **Jellyfin Web**.
2. **Anuncios antes de reproducir (pre-roll)** — vídeos de tu biblioteca que se reproducen
   antes de cada película o episodio, **también en apps nativas** (Android APK, Android TV,
   Roku…) mediante `IIntroProvider`.

Cada sistema tiene su propia sección de configuración y su propia lista de anuncios.

## Instalación por catálogo

Añade este repositorio en **Dashboard → Complementos → Repositorios**:

```
https://raw.githubusercontent.com/franciscocolon22/Jellyfin-Startup-Ads/main/manifest.json
```

Luego instala **Jellyfin Startup Ads** desde el catálogo y reinicia Jellyfin.

## Versiones en este repositorio

| Carpeta | Estado |
|---|---|
| [`V1.4.0/`](V1.4.0/) | **Versión actual.** Añade el sistema pre-roll. |
| [`V1.3/`](V1.3/) | Snapshot congelada de la v1.3.4 (solo overlay web). |

La documentación completa está en [`V1.4.0/README.md`](V1.4.0/README.md) y
[`V1.4.0/docs/CONFIGURACION.md`](V1.4.0/docs/CONFIGURACION.md).
