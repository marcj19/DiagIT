# Como contribuir com o DiagIT

Obrigado por querer ajudar! O DiagIT existe para que qualquer pessoa descubra **com prova** o que está errado no PC e saiba o que fazer.
Contribuições de quem entende de hardware e de quem só passou por um problema são igualmente úteis.

## Princípios (leia antes de propor uma regra)

1. **Prova antes de palpite.** Um achado só pesa contra uma peça se houver evidência medida (teste reprovado, erro registrado pelo próprio
   hardware, falha que se repete) ou uma correlação clara com problemas registrados. Dicas genéricas de "otimização" não viram achado.
2. **Linguagem simples primeiro.** Todo achado tem um título que um leigo entende; os detalhes técnicos ficam em "Ver detalhes técnicos".
3. **Nada sai do PC.** Sem telemetria, sem envio de dados, sem downloads automáticos.
4. **Funciona em qualquer marca.** Nada de texto preso a um PC específico: use os marcadores de fabricante (abaixo).
5. **Correções sempre pedem confirmação** e, quando possível, têm como desfazer.

## Contribuir sem programar: as regras em `data/`

O conhecimento do DiagIT fica em arquivos JSON na pasta [`data/`](data). Dá para melhorar o diagnóstico editando só esses arquivos:

| Arquivo | O que guarda |
|---|---|
| [`telas-azuis.json`](data/telas-azuis.json) | Cada código de tela azul: nome, frase simples, conselho, peso contra cada peça e qual parâmetro aponta o driver culpado |
| [`fabricantes.json`](data/fabricantes.json) | Textos por marca (AMD, NVIDIA, Intel): painel do driver, site de download, nomes dos ajustes de BIOS |
| [`placas-de-video.json`](data/placas-de-video.json) | Clock de boost oficial das placas |
| [`programas.json`](data/programas.json) | Programas em segundo plano que a comunidade associa a engasgos |
| [`linguagem-simples.json`](data/linguagem-simples.json) | Tradução dos títulos técnicos para linguagem simples |
| [`drivers.json`](data/drivers.json) | O que é cada driver que aparece como culpado de tela azul (`nvlddmkm.sys` → driver de vídeo da NVIDIA) e o conselho para cada tipo |
| [`guias.json`](data/guias.json) | Soluções passo a passo dentro do app (texto, botões de ação) e quais achados cada guia resolve; ao concluir, a correção entra no acompanhamento "corrigiu?" |

Nos textos, use os marcadores, que o DiagIT troca pelo nome certo para cada PC:
`{painel_video}`, `{site_driver}`, `{nome_ddu}`, `{perfil_memoria}`, `{ajustes_cpu}`.

**Para testar uma edição sem compilar:** crie uma pasta `data` ao lado do `DiagIT.exe` com os JSON editados (ela substitui as regras
embutidas) e rode `DiagIT.exe --diag-test saida.txt`. O teste confere se tudo carrega, se os pesos estão entre 0 e 1, se não há códigos
repetidos nem marcadores desconhecidos.

Sempre cite a fonte de uma regra nova no pull request (documentação da Microsoft, do fabricante, relato com evidência).

## Contribuir com código

- **Compilar:** `powershell -ExecutionPolicy Bypass -File build.ps1 -OutDir build\dev` (usa o compilador C# que já vem no Windows,
  .NET Framework 4.8; não precisa instalar nada).
- **Testes de lógica:** `build\dev\DiagIT.exe --diag-test saida.txt` precisa terminar com `TUDO OK`. Ao mudar o diagnóstico, adicione um
  cenário em `src/SelfTest.cs` (método `DiagScenarios`).
- **Arquivos de tela azul:** `DiagIT.exe --dump-info C:\Windows\Minidump saida.txt` (como administrador) mostra o código, o driver culpado e a pilha que o DiagIT extrai de cada arquivo.
- **Revisar o visual:** `build\dev\DiagIT.exe --multi --offscreen --no-elevate --screenshots pasta` salva uma imagem de cada tela.
- **Estilo:** C# 5 (o compilador do Windows não aceita recursos mais novos: sem `$"..."`, `?.`, `=>` em membros), textos da interface em
  português do Brasil, comentários curtos explicando o porquê.
- **Onde fica cada coisa:** `src/Core` (coleta e análise, sem interface), `src/Tests` (testes de hardware), `src/UI` (telas).
  As regras de análise estão em `src/Core/Analyzer*.cs`; o texto simples em `AnalyzerPlain.cs`.

O GitHub compila e roda o `--diag-test` em todo pull request.

## Reportar um problema

Use os modelos em *Issues*. O mais útil é o **relatório HTML** (Resultados → Exportar relatório HTML): ele já traz o hardware,
os testes e as evidências. Revise antes de anexar: ele mostra o modelo das peças e os nomes dos jogos gravados.

## Licença

Ao contribuir, você concorda que sua contribuição seja distribuída sob a [GPL-3.0](LICENSE), a mesma do projeto.
