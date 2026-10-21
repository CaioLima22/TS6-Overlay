# TS Overlay

Overlay para o TeamSpeak 6 que mostra quem está no seu canal e quem está falando, como o overlay do Discord.

Roda como uma janela externa por cima do jogo, sem injeção no processo do jogo (sem risco de anti-cheat). Por isso funciona com o jogo em **janela sem borda**; tela cheia exclusiva não é suportada.

## Status

Fase 1 (MVP): conecta ao TS6, lista o canal atual, acende o nome de quem fala e mostra os ícones de mute. Por enquanto é uma janela comum sempre no topo; transparência e click-through chegam na fase 2.

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

```
dotnet build TsOverlay.sln
dotnet test tests/TsOverlay.Tests
```

Para ver as mensagens brutas da API (útil quando uma beta nova muda algo), rode com `--debug` e acompanhe a saída de depuração.
