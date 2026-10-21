# TS Overlay

Overlay para o TeamSpeak 6 que mostra quem está no seu canal e quem está falando, como o overlay do Discord.

Roda como uma janela externa por cima do jogo, sem injeção no processo do jogo (sem risco de anti-cheat). Por isso funciona com o jogo em **janela sem borda**; tela cheia exclusiva não é suportada.

## Status

- Fase 1 (MVP): conecta ao TS6, lista o canal atual, acende o nome de quem fala e mostra os ícones de mute.
- Fase 2 (overlay): janela transparente, sempre no topo, fora da barra de tarefas e do Alt+Tab, que nunca rouba o foco do jogo.
- Fase 3 (conforto): ícone na bandeja, modo "só quem fala", overlay só nos apps escolhidos e iniciar com o Windows.
- Fase 4 (releases): instalador pelo GitHub Releases e atualização automática pelo Velopack.

## Usando o overlay

- **Destravado** (borda azul): arraste para mover, use Ctrl + roda do mouse para mudar a opacidade e o ✕ para fechar.
- **Travado**: os cliques passam direto para o jogo.
- **Ctrl+Shift+O** (padrão) alterna entre travado e destravado, mesmo com o jogo em foco.
- Para trocar o atalho: destrave, clique no botão do atalho e pressione a nova combinação (Esc cancela). Se outro programa já usa o atalho, o overlay abre destravado e avisa.

### Bandeja

O ícone do TS Overlay na bandeja (ao lado do relógio) tem:

- **Ocultar/Mostrar overlay** (ou clique duplo no ícone). O ✕ do overlay também só oculta.
- **Travar/Destravar**, igual ao atalho.
- **Mostrar só quem está falando**: a lista fica só com quem fala. Travado e sem ninguém falando, o overlay some.
- **Mostrar só nos apps escolhidos** + **Apps e jogos**: marque em quais programas o overlay aparece. A lista mostra os apps abertos agora e os que você já marcou. Destravado, o overlay sempre aparece para dar para posicioná-lo.
- **Iniciar com o Windows**: registra o executável atual em `HKCU\...\Run`. Ative pelo `.exe` que você vai usar no dia a dia.
- **Sair**.

Posição, opacidade, o estado travado e o atalho ficam salvos para a próxima vez. Se a posição salva estiver fora da tela (um monitor desconectado, por exemplo), o overlay volta para o canto superior esquerdo.

## Instalação

Baixe o `TsOverlay-win-Setup.exe` da [última release](https://github.com/CaioLima22/TS6-Overlay/releases/latest) e rode. Ele instala em `%LocalAppData%\TsOverlay`, cria atalhos e, a partir daí, o app se atualiza sozinho: checa ao abrir e a cada 6 horas, baixa em segundo plano e aplica quando você sai (ou na hora, pela bandeja).

## Como usar

1. No TeamSpeak 6: **Configurações → Remote Apps**, ative e deixe a porta **5899**.
2. Rode o TS Overlay (`dotnet run --project src/TsOverlay`).
3. Na primeira vez, aprove o "TS Overlay" na notificação do TeamSpeak. A chave fica salva em `%AppData%\TsOverlay\settings.json` e não é pedida de novo.

Se o TS estiver fechado, o overlay tenta reconectar a cada 2 segundos.

## Estrutura

```
src/TsOverlay.Core/   → lógica sem UI (roda e testa em qualquer SO)
  Ts/TsRemoteClient.cs    WebSocket, auth, reconexão
  Ts/TsMessages.cs        leitura tolerante do JSON da API
  State/ChannelState.cs   quem está no meu canal e quem fala
  Settings/AppSettings.cs configurações em %AppData%
src/TsOverlay/        → app WPF (Windows)
tests/TsOverlay.Tests → testes do ChannelState e do cliente
```

## Desenvolvimento

### Publicar uma versão

```
git tag v0.2.0
git push origin v0.2.0
```

O workflow `Release` roda os testes, publica, empacota com o Velopack e cria a release no GitHub. Os apps instalados pegam a versão nova sozinhos.

```
dotnet build TsOverlay.sln
dotnet test tests/TsOverlay.Tests
```

Para ver as mensagens brutas da API (útil quando uma beta nova muda algo), rode com `--debug` e acompanhe a saída de depuração.
