# Changelog

## 0.3.0 - 2026-11-17

Primeira versão deste fork (barra-tarefas), baseado no OpenTaskbar 0.2.6 (MIT, OpenTaskbar contributors).

- **Visual novo em blocos (padrão):** barra em largura total flutuando a 8 px das bordas, fundo quase preto translúcido com borda fina; cada ícone num bloco arredondado de 44 px; ponto azul sob os apps abertos; botão Iniciar com o logo do Windows em cinza.
- **Grupos de apps:** um bloco com até 4 mini-ícones que abre um painel com os apps. Crie, renomeie e desfaça pelo botão direito.
- **Cards:** novo card de calendário (dia da semana, dia e próximo evento — "Sem eventos" até ligarmos um calendário); botão "^" da bandeja antes do relógio; relógio com dia da semana em vermelho, data, hora grande e clima.
- **Português (Brasil):** todo o app traduzido; abre em português automaticamente quando o Windows está em português.
- **Configurações:** novas opções "Ícones em blocos" e "Mostrar o clima abaixo da hora". Configurações antigas migram uma vez para o visual novo, mantendo apps fixados, perfis e pilhas.
- **Correção:** o app fechava sozinho quando um player de mídia fechava ou ainda não tinha dados de reprodução.
- **Correção:** a janela de Configurações usava só metade da largura.
- **Atualizações:** a verificação automática agora olha as releases deste repositório (não mais as do projeto original).
## 0.2.6 - 2026-11-17

- Added automated in-place update downloader and restarter; updates download silently in the background and replace the executable without browser redirects.
- Replaced intrusive dock status messages with native Windows Action Center toast notifications for update alerts and system feedback.
- Added Task Manager launch command to the dock context menu with system path resolution and dedicated vector icon.
- Added full support for Microsoft Store and packaged application icons in running dock entries and `ApplicationFrameHost` instances.
- Fixed dock layout delay so closed applications immediately shrink the dock without leaving blank placeholder cells.

## 0.2.5 - 2026-11-17

- Added custom, theme-integrated `ComboBox` and `ComboBoxItem` styles matching OpenTaskbar's acrylic glass design tokens.
- Expanded Weather flyout with humidity, wind speed, today's high/low temperatures, and dynamic condition-based background gradients.
- Added a 3-day weather forecast with day titles, condition icons, localized status descriptions, and high/low temperatures.
- Widened Settings window default bounds (`980x680`) and content metrics (`SettingsReadingWidth` to 960 DIP, `SettingsPageInset` to 32,20,24,20) for optimal 3-column widget card wrapping.
- Refined vertical scrollbars with a slender 6 DIP footprint and minimal thumb margin.

## 0.2.4 - 2026-11-16

- Refactored and modernized project architecture: partitioned windows into `Views/Windows/`, popups into `Views/Popups/`, and system services into `Services/Interop/`, `Services/Settings/`, and `Services/App/`.
- Decomposed `MainWindow` into focused partial classes for magnification, drag-and-drop, and resizing.
- Renamed data model item to `DockWidgetItem` to eliminate ambiguous name collision with widget UI base class.
- Added downward-pointing chevron buttons on configurable widget cards in Settings to expand and collapse widget-specific configuration drawers with automatic scrolling.
- Replaced the manual Apply footer with a full real-time auto-save system across all preferences and widget settings.
- Added graphics card usage, graphics card temperature and processor temperature meters to System meters, each with its own toggle.
- Added country and city selection picker for weather forecasts.
- Added automatic detection and hiding of the dock when full-screen browser videos, games, or borderless windows are active.
- Added fluent open/close and tab transition animations to the Settings window with bounded min/max dimensions.
- Highlighted the foreground application independently of keyboard focus, clearing its background when minimized or another app becomes active.
- Added normal close and confirmed process termination to background tray apps.
- Updated README documentation with authentic desktop hero showcase and visual feature gallery.

## 0.2.3 - 2026-11-16

- Kept application icons at 42 DIP inside fixed 52 DIP cells when resizing or adding widgets.
- Allowed the dock to narrow to one visible application cell, deriving its minimum width from the frame insets.
- Removed the visible horizontal scrollbar; Shift/Ctrl plus wheel and middle-button dragging scroll overflowing content.
- Replaced decorative resize grips with invisible hit targets at the outside left and right borders.
- Kept auto-hide locked during resizing and middle-button panning.

## 0.2.2 - 2026-11-16

- Kept magnified and bouncing icons inside the dock's scroll viewport while preserving horizontal clipping.
- Reserved the missing shelf height above Explorer's existing bottom space, without adding hover headroom to the desktop reservation.
- Updated the reservation when auto-hide changes and prevented recursive Shell positioning updates.
- Kept maximized captioned windows distinct from full-screen applications and released the extra reservation on exit.

## 0.2.1 - 2026-11-16

- Replaced text-heavy widget catalog rows with a wrapping gallery of real dock widget previews.
- Install widgets by selecting their preview; keep visibility and removal controls on installed items.
- Refresh local preview data while the widget settings page is visible, without running timer actions.

## 0.2.0 - 2026-11-16

- Refined dark and light dock surfaces, quieter resize handles, and softer shadows.
- Added an optional widget catalog with install, remove, show, hide, and localized search.
- Added clock, date, battery, memory, system-drive space, day progress, stopwatch, and countdown widgets.
- Added compact time tiles and shared details panels for duration input and reset.
- Improved widget catalog readability and themed input, focus, and read-only states.
- Standardized application icon bounds and added two-sided dock resizing with horizontal scrolling.
- Used the Windows UI language for initial preferences while preserving saved language choices.
- Added deterministic behavior checks and rendered WPF component checks.

## 0.1.0 - 2026-11-15

- Initial portable Windows x64 release.
