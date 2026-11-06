# Publicar una versión en GitHub Releases

El instalador se genera con [Velopack](https://velopack.io) mediante `build-installer.ps1`.
El usuario final no necesita instalar .NET ni tener permisos de administrador.

## 1. Subir la versión

En `LectorArchivos/LectorArchivos.csproj` cambia `<Version>` (por ejemplo de `1.0.0` a `1.0.1`).
Cada release debe tener una versión mayor que la anterior.

## 2. Generar el instalador

Desde la carpeta de la solución, en PowerShell:

```powershell
.\build-installer.ps1
```

(Si PowerShell bloquea el script: `powershell -ExecutionPolicy Bypass -File .\build-installer.ps1`)

Se crean en `artifacts\releases\`:

| Archivo | Para qué |
|---|---|
| `LectorArchivos-win-Setup.exe` | **Instalador.** Doble clic e instala para el usuario actual, con accesos directos en el escritorio y el menú Inicio. Se desinstala desde *Configuración → Aplicaciones*. |
| `LectorArchivos-win-Portable.zip` | Versión portable: se descomprime y se ejecuta `Lector de Archivos.exe`, sin instalar. |
| `releases.win.json`, `*.nupkg`, `assets.win.json` | Metadatos de Velopack (solo hacen falta si en el futuro se añaden actualizaciones automáticas). |

## 3. Crear la release en GitHub

1. Haz commit y `git push` de los cambios.
2. En GitHub abre **Releases → Draft a new release**
   (<https://github.com/Reonburakku/LectorArchivos/releases/new>).
3. En **Choose a tag** escribe `v1.0.1` (la misma versión) y pulsa **Create new tag**.
   Elige la rama de destino (por ejemplo `master`).
4. Título: `Lector de Archivos v1.0.1`. Escribe las novedades en la descripción.
5. Arrastra a **Attach binaries** los archivos `LectorArchivos-win-Setup.exe` y
   `LectorArchivos-win-Portable.zip`.
6. Pulsa **Publish release**.

## Para quien lo descarga

- Descarga `LectorArchivos-win-Setup.exe` y ábrelo.
- Como el instalador no está firmado digitalmente, Windows SmartScreen mostrará
  «Windows protegió su PC»: pulsa **Más información → Ejecutar de todas formas**.
- La primera vez, en **⚙ Configurar IA** hay que poner una API key gratuita de Google Gemini
  (<https://aistudio.google.com/apikey>). La clave se guarda cifrada solo en ese PC.
