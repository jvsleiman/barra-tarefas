# Changelog

## 0.4.2 - 2026-11-17

- **Configurações mais limpas:** os interruptores grandes de liga/desliga viraram uma marcação pequena e discreta, e as linhas perderam as divisórias.
- **Chips em vez de listas de interruptores:** os itens das configurações rápidas e os serviços do card de IA agora são chips que acendem quando estão ativos.
- **Ordem dos blocos:** a lista mostra só as setas ↑/↓; os cards continuam sendo ligados e desligados na página Widgets.
## 0.4.1 - 2026-11-17

- **Dispositivos sem fio:** a busca agora usa a lista de dispositivos Bluetooth pareados e conectados do Windows, e um dispositivo com erro não esconde mais os outros. O card não some quando nada está conectado: mostra "Nenhum dispositivo" (antes parecia que o widget não tinha sido adicionado).
- **Uso de IA mais visual:** cada serviço virou um card com o logo e dois anéis pequenos (5h e 7d) só com a %; no Claude Code o anel mostra os tokens. Tempo para renovar e detalhes ficam na dica ao passar o mouse.
- **Ordem dos blocos:** Configurações › Barra › "Ordem dos blocos" muda a ordem dos cards da barra (relógio, bandeja, calendário, widgets) com ↑/↓ e liga/desliga cada um. Também dá para arrastar os cards direto na barra.
- **ChatGPT e Grok no card de IA:** como esses apps não guardam dados de uso no PC, aparecem como atalho com o logo (abre o app ou o site) e "Sem dados de uso" na dica; o Codex continua com a % real. Ative em Configurações › Barra › Uso de IA.
- **Botão Iniciar:** nova opção "Ícone do Windows…", com uma galeria dos ícones do próprio Windows (imageres.dll e shell32.dll) e a foto da conta.
## 0.4.0 - 2026-11-17

Feita a partir das suas sugestões.

- **Cores e personalização** (Configurações › Aparência): cor de destaque, fundo da barra, cor e opacidade dos blocos, arredondamento da barra, cor do dia da semana e do ponto de app aberto, com prévia ao vivo e "Restaurar cores padrão".
- **Botão Iniciar:** logo cinza, logo do Windows, imagem sua (PNG/ICO/JPG) ou o texto "Iniciar", em tamanho ajustável.
- **Nova página "Barra":**
  - **Apps abertos:** mostrar o nome do app ao lado do ícone, indicador em ponto ou barra, e só os apps desta tela.
  - **Configurações rápidas:** escolha quais botões aparecem (Wi-Fi, Bluetooth, projetar tela, hotspot, painel do Windows, notificações), em que ordem, e o modo compacto só com ícones. O padrão agora é mais limpo: Wi-Fi, Bluetooth e notificações.
  - **Pastas** (antes "grupos"): juntam apps fixados num bloco; agora com cor. Crie pelo botão direito num app fixado › Criar pasta.
  - **Uso de IA:** escolha se o card mostra ChatGPT (Codex), Claude Code ou os dois.
- **Widgets novos:**
  - **Dispositivos sem fio:** fone, mouse, teclado, controle e celular conectados por Bluetooth, com a bateria em % quando o aparelho informa.
  - **Velocidade da rede:** download e upload em tempo real.
  - **Uso do disco:** barra de ocupação do disco do sistema e espaço livre.
  - **Notas rápidas:** um bloquinho que abre da barra e salva sozinho em `%LOCALAPPDATA%\OpenTaskbar\notas.txt`.
  - **Uso de IA:** "Sessão" e "Semanal" com barras verdes. ChatGPT (Codex) mostra a % real e o tempo até renovar, a partir dos logs do Codex CLI. Claude Code mostra os tokens do bloco de 5 horas e dos últimos 7 dias, a partir dos logs do Claude Code. Tudo lido de arquivos do PC, sem login nem token.
## 0.3.2 - 2026-11-17

- **Procurar atualização:** novo botão em Configurações › Geral › "Inicialização e atualizações" e no menu do botão direito da barra. Mostra "Versão X disponível" com "Atualizar agora" (baixa, confere o SHA-256 e reinicia já atualizado) ou "Você já está na versão mais recente".
- **Repositório privado:** quando não dá para conferir sozinho, aparece "Não consegui verificar automaticamente" com "Abrir página de downloads" e "Instalar de um arquivo…" (escolha o OpenTaskbar.exe baixado; o app confere se é mesmo uma versão mais nova do OpenTaskbar antes de instalar).
- **Sugestões:** "Adicionar sugestão…" no menu da barra e uma página "Sugestões" nas Configurações. Cada sugestão é salva com data, tipo (Ideia, Bug, Visual) e versão em `%LOCALAPPDATA%\OpenTaskbar\sugestoes.md`, que as atualizações não apagam. Na página dá para marcar como feita, excluir, abrir o arquivo e copiar tudo.
- Mensagens de erro da atualização que apareciam em turco agora aparecem em português.
## 0.3.1 - 2026-11-17

- **Botão Iniciar:** o segundo clique agora fecha o menu Iniciar (antes fechava e abria de novo na hora).
- **Painéis da barra (grupo, Downloads, bandeja, relógio, calendário):** o segundo clique fecha o painel. Antes, o clique chegava durante a animação de fechar e reabria o painel; segurar o clique um pouco mais também reabria. O painel também não fecha mais sozinho logo depois de abrir.
- **Card do calendário:** agora abre o calendário do mês (igual ao do relógio), que abre e fecha no mesmo bloco; as notificações do Windows continuam a um clique dentro dele.
- **Encerrar tarefa:** encerra de verdade apps em segundo plano, inclusive todos os processos do app. Apps que rodam como administrador, serviços do Windows e processos essenciais (como o Explorer) mostram uma mensagem clara em vez de falhar calado; se algo continuar aberto, a barra avisa.
- **Fechar:** fecha também apps que ficam só na bandeja (sem janela visível).
- **Prévia das janelas:** ao fechar uma das janelas, a prévia se atualiza em vez de mostrar a janela que já fechou.
- **Configurações mais simples:** a página Geral agora tem seções (Barra, Inicialização e atualizações, Ocultar automaticamente), uma linha explicando cada opção e as opções pouco usadas em "Mostrar opções avançadas". Textos que ainda apareciam em turco ou inglês foram traduzidos.
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
