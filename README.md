# Órbita

Tarefas e rotinas em círculos concêntricos: o centro é o que importa agora, e os anéis de fora são o que pode esperar. Tem também um calendário circular com os dias do mês, as 24 horas do dia e as rotinas.

- **Site:** https://joaogabrielmontinirossi-sys.github.io/orbita/
- **Celular:** abra o site no navegador do celular e escolha "Adicionar à tela inicial" (Android: menu ⋮ do Chrome; iPhone: botão Compartilhar do Safari). Funciona sem internet depois da primeira abertura.
- **Windows:** baixe o `Orbita.exe` na página de [Releases](https://github.com/joaogabrielmontinirossi-sys/orbita/releases). Ele abre o app numa janela própria e usa o Microsoft Edge que já vem no Windows.

## Sincronização

No app de Windows, o botão **Sincronização** grava o arquivo `orbita-sync.json` numa pasta do Google Drive a cada alteração. Outro computador com o Órbita e o mesmo Drive recebe tudo automaticamente; alterações feitas nos dois lados são mescladas item por item.

No site e no celular os dados ficam só no navegador daquele aparelho.

## Código

- `index.html`, `sw.js`, `manifest.webmanifest`, `icons/`: o site publicado.
- `src/orbita.html`: a fonte do app. `src/build.ps1` gera o site, os ícones e compila `src/windows/Orbita.cs` com o compilador C# que já vem no Windows.
