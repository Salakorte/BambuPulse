# Bambu P1S Monitor — dirección visual

## Intención
Un panel compacto de impresora, siguiendo la jerarquía de Bambu Bar: estado en una cápsula, impresora junto a la impresión, progreso y tiempos en el mismo bloque, temperaturas en tiles y AMS en una sola banda. La biblioteca propia y el acceso Bambu Cloud se conservan.

## Tokens
- Fondo: #101113; superficies: #202020 y #2B2B2B; bordes suaves: #3B3B3B.
- Texto principal: #F5F5F7; secundario: #D7D8DA; auxiliar: #9EA0A4.
- Verde Bambu #21CC6B para progreso, impresión y acciones principales.
- Estados: preparado azul #78AEDA, pausa ámbar #FFB83D, error #FF5C5C; boquilla naranja y cama dorada.
- Tipografía: Segoe UI. Escala: título 20–22 px, sección 10–13 px, cuerpo 9–10 px, metadatos 8 px; botones 9 px semibold.
- Espaciado: pasos de 4, 8, 12, 16, 24 y 32 px; radios consistentes de 12–16 px.

## Componentes y flujo
El bloque principal coloca la ilustración P1S a la izquierda y trabajo, porcentaje, capas y ETA a la derecha. La ventana usa marco propio, esquinas redondeadas y cabecera arrastrable. Temperaturas en tarjetas planas con actual, objetivo y barra. Las cuatro bobinas se agrupan en una banda con muestra circular, tipo de material, gramos y nivel. Los avisos de consumo pendiente mantienen su color semántico y acceso a Biblioteca.

## Referencias aplicadas
Bambu Bar y su port para Windows sirven como referencia directa de composición: panel de estado, hero de trabajo, métricas compactas y tira AMS. Su código está publicado bajo licencia MIT. Se adapta la jerarquía a las funciones locales de esta app, incluida Biblioteca y la sesión Cloud.



