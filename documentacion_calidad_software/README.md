# Documentación Académica — Calidad de Software
## Sistema de Inventariado Hotelero Multi-Tenant (CONTINENTALIA SAS)

Esta carpeta contiene los entregables académicos en formato LaTeX correspondientes a la asignatura **Calidad de Software**, elaborados a partir de la inspección directa del código fuente del repositorio (`backend/`, `frontend/hotel-management-web/`, `database/` y suites de pruebas automatizadas).

---

## Estructura de Entregables

```text
documentacion_calidad_software/
├── informe_calidad_software.tex          # Documento principal editable en LaTeX (18 secciones + portada)
├── referencias.bib                       # Bibliografía en formato BibTeX / APA 7
├── README.md                             # Instrucciones de compilación y estructura
└── diagramas/                            # Diagramas técnicos TikZ incluidos en el documento
    ├── 01_arquitectura_general.tex       # Arquitectura general Clean Architecture + Angular 22 + SQL Server
    ├── 02_arquitectura_multitenant.tex   # Flujo de aislamiento Multi-Tenant en tres barreras
    ├── 03_casos_de_uso.tex               # Diagrama de casos de uso según matriz RBAC real
    ├── 04_flujo_inventariado.tex         # Flujo del proceso de inventariado hotelero y estados
    ├── 05_diagrama_componentes.tex       # Diagrama de componentes Frontend, Backend y Persistencia
    └── 06_modelo_entidad_relacion.tex    # Modelo Entidad-Relación de las 22 tablas de SQL Server
```

---

## Nota sobre `informe_calidad_software.pdf`

Se verificó el entorno local de ejecución (`Windows`) mediante búsqueda en el `PATH` y en los directorios del sistema (`C:\Program Files`, `%LOCALAPAPDATA%\Programs`), comprobándose que **no se encuentra instalada una distribución LaTeX local (`pdflatex`, `lualatex`, MiKTeX o TeX Live)** en esta máquina. Conforme a la instrucción de entrega (*"5. informe_calidad_software.pdf: Generarlo únicamente si el entorno dispone de un compilador LaTeX funcional"*), se entregan todos los archivos fuente `.tex` y `.bib` verificados sintácticamente y listos para compilar.

---

## Instrucciones de Compilación (pdfLaTeX / LuaLaTeX / Overleaf)

### Opción 1: Compilación local con TeX Live o MiKTeX
Ubicarse dentro del directorio `documentacion_calidad_software/` y ejecutar la secuencia estándar de compilación para resolver referencias cruzadas, tabla de contenido, lista de figuras, lista de tablas y bibliografía:

```bash
cd documentacion_calidad_software
pdflatex -interaction=nonstopmode informe_calidad_software.tex
bibtex informe_calidad_software
pdflatex -interaction=nonstopmode informe_calidad_software.tex
pdflatex -interaction=nonstopmode informe_calidad_software.tex
```

O utilizando `lualatex`:

```bash
cd documentacion_calidad_software
lualatex -interaction=nonstopmode informe_calidad_software.tex
bibtex informe_calidad_software
lualatex -interaction=nonstopmode informe_calidad_software.tex
lualatex -interaction=nonstopmode informe_calidad_software.tex
```

### Opción 2: Compilación en Overleaf
1. Crear un proyecto nuevo en [Overleaf](https://www.overleaf.com/).
2. Subir `informe_calidad_software.tex`, `referencias.bib` y la carpeta `diagramas/` con sus 6 archivos `.tex`.
3. Establecer `informe_calidad_software.tex` como documento principal (*Main document*) y compilar con `pdfLaTeX` o `LuaLaTeX`.

---

## Personalización de la Portada
Al inicio de `informe_calidad_software.tex` se encuentran definidos comandos editables para completar los datos institucionales antes de la entrega final:

```latex
\newcommand{\Institucion}{[NOMBRE DE LA INSTITUCIÓN UNIVERSITARIA]}
\newcommand{\ProgramaAcademico}{[PROGRAMA ACADÉMICO — INGENIERÍA DE SOFTWARE / SISTEMAS]}
\newcommand{\Docente}{[NOMBRE DEL DOCENTE]}
\newcommand{\IntegranteUno}{[NOMBRE DEL INTEGRANTE 1 — CÓDIGO]}
\newcommand{\IntegranteDos}{[NOMBRE DEL INTEGRANTE 2 — CÓDIGO]}
\newcommand{\IntegranteTres}{[NOMBRE DEL INTEGRANTE 3 — CÓDIGO]}
```
