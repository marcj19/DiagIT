# DiagIT — diagnóstico de hardware com testes comprovados

Aplicativo para Windows que **monitora as partidas em segundo plano**, **testa cada peça do PC sob carga com verificação de
resultado** e **aponta o componente com problema comprovado**, junto com o que fazer para resolver.

Feito em C# / Windows Forms (.NET Framework 4.8, que já vem no Windows). Não precisa do HWiNFO nem de outro programa aberto.

## Como usar

Feito para quem entende de hardware **e** para quem só quer resolver o problema: tudo aparece primeiro em linguagem simples,
com os detalhes técnicos (códigos, eventos, medições) a um clique em *Ver detalhes técnicos*.

1. Abra `dist\DiagIT.exe` (ele pede permissão de administrador — necessária para ler a CPU, a placa-mãe, o SMART dos discos e os arquivos de tela azul).
2. **Jogue normalmente.** O DiagIT fica na bandeja, detecta o jogo sozinho, grava a partida e avisa no fim se viu algo errado.
3. **Início**: o veredito do seu PC em uma frase, o anel com as 8 peças (cada segmento é uma peça, na cor do estado) e *O que fazer agora*.
4. **Estou com um problema**: escolha o que acontece (tela azul, desliga sozinho, jogo fecha, travadinhas, tela preta, FPS baixo).
   O DiagIT mostra o que já sabe, monta a verificação certa (sem repetir o que já passou) e acompanha tudo na tela *Verificando*.
5. **Resultados**: cada peça como **Saudável**, **Precisa verificar**, **Com defeito** ou **Não verificado** (sempre com ícone, cor e texto),
   e cada achado como **Comprovado**, **Indício** ou **Informação**, com *O que fazer*.
   Um indício anterior ao teste aprovado não deixa a peça suspeita; se o problema voltar depois da aprovação, o DiagIT pede para repetir o teste.
6. **Resolver**: aplica as soluções com um clique (sempre pedindo confirmação).
   Os achados principais têm **Resolver passo a passo** (guias dentro do app, ex.: reinstalar o driver de vídeo do jeito certo, voltar a BIOS
   para o padrão) e **Já fiz isso**. Toda correção entra em **Resolveu?**, no topo do Início: o DiagIT compara antes e depois e diz se **resolveu**
   (nenhuma ocorrência por pelo menos 7 dias de uso, com uma barra mostrando o progresso), se está **acompanhando**, ou se **o problema voltou**.
   Quando volta, o cartão mostra o que estava acontecendo na hora (ex.: um jogo fechou junto) e um botão leva ao **plano de testes**: em vez de repetir o DDU,
   ajustes reversíveis de Windows, um de cada vez (sobreposições de vídeo/MPO, HAGS, tempo do TDR, gravação em segundo plano, turbo do processador), cada um com
   botão **Desfazer** e acompanhado separadamente. Há também guias para processador instável e processador quente.
   Um reset de vídeo registrado pelo Windows (LiveKernelEvent) **sem nada visível na mesma hora** conta como informação; só vira "voltou" com sintoma
   (TDR, tela azul, desligamento ou programa que caiu dentro do driver de vídeo). Um programa que fecha **uma vez** com erro no módulo dele mesmo pode ser só
   um defeito do programa: só passa a contar se se repetir.

### Verde com prova, sem alarme à toa

O DiagIT mostra "Seu PC está saudável" quando as peças passaram nos testes e nada pede ação, mesmo havendo avisos de rotina, que continuam visíveis como **Informação**.
Regras que mantêm o verde honesto (e o alarme verdadeiro quando há problema real):
- **Informação nunca pontua** nem vira "indício"; uma peça só fica em "Precisa verificar" com pelo menos uma evidência de peso, e só um sintoma real (tela azul, TDR, queda no driver, desligamento) passa a suspeita para "Drivers e Windows" quando o teste da peça não reproduz o problema.
- **Os testes do próprio DiagIT não julgam o dia a dia**: calor, VRAM cheia, CPU a 100%, RAM travada e disco lido inteiro são esperados num teste. Temperatura, ventoinha, VRAM, congelamentos e gargalo usam só partidas de jogo; o clock é comparado partida por partida e precisa se repetir.
- **Limites por projeto do chip**: processadores atuais trabalham a ~95 °C e placas de vídeo chegam a ~110 °C no hotspot; abaixo disso não é defeito. Superaquecimento comprovado continua sendo o teste abortado por temperatura. Ventoinha lida como 0 RPM (sensor sem suporte, water cooler) e pistas PCIe reduzidas são indício, não "comprovado".
- **Depois de uma troca de driver**, o que aconteceu antes dela é histórico do driver antigo e não entra na conta atual.
- **Picos isolados** de disco, memória virtual instantânea (com paginação automática), dispositivos USB/HID/Bluetooth sem driver e a falta de sensor de temperatura são informação.
- **Programas que não são jogo** (TeamSpeak, Discord, navegadores...) nunca são tratados como jogo.
- O aviso no fim de cada partida mostra só o que é novo **dessa** partida (e o FPS), não os alertas dos últimos 30 dias.
- "Resolveu?" conta antes e depois do mesmo jeito (só com sintoma) e um achado resolvido vira histórico.
   Para defeitos que aparecem de vez em quando, o **check-up à noite** (3 h ou 8 h) testa por horas e impede o PC de dormir.
7. **Avançado**: *Ao vivo* (gráficos em tempo real), *Hardware* (tudo que foi lido do PC) e *Partidas gravadas*. Testes individuais e mais longos ficam em *Verificar peças*.

## Testes (resultado verificável, não estimativa)

| Teste | O que faz | Reprova quando |
|---|---|---|
| Processador | Todos os núcleos (fixados um a um) calculam pacotes determinísticos com AVX/SIMD, inteiros, raiz, divisão e cache, conferindo cada resultado bit a bit com a referência | Qualquer cálculo diferente (CPU instável), temperatura crítica, erro WHEA durante o teste |
| Memória RAM | Trava na RAM física a memória livre e grava/confere 5 padrões (endereço, inverso, xadrez, aleatório, *march*) em várias passadas | Qualquer bit diferente do gravado (mostra endereço e quais bits) |
| Placa de vídeo e VRAM | Direct3D 11 Compute: carga total com cálculo repetido e comparado, padrões em toda a VRAM livre, banda PCIe medida | Resultado divergente, erro de VRAM, **GPU trava** (o mesmo "driver parou de responder" dos jogos) |
| Armazenamento | SMART/NVMe lido direto do disco + leituras espalhadas (ou completas) pelo disco inteiro, somente leitura | Setor ilegível, alerta do próprio disco, erros de mídia/CRC |
| Fonte de energia | CPU e GPU no máximo ao mesmo tempo (maior consumo possível), monitorando +12 V | O PC desliga/reinicia (detectado ao reabrir, com consumo e temperaturas do momento) |

Durante todos os testes são registradas temperaturas, consumo, clocks, tensões (+12/+5/+3,3 V), ventoinhas e link PCIe. O teste para
sozinho se a temperatura ficar crítica (e isso já é o resultado: refrigeração insuficiente). **O DiagIT recusa iniciar testes com jogo aberto.**

## Relatório e pacote para o técnico

Em **Resultados**: **Relatório** abre um relatório imprimível (salva em PDF pelo navegador) com uma página em linguagem simples para o dono
do PC e a parte técnica: sistema (Windows com build, BIOS, memória, discos), linha do tempo de 30 dias, cada tela azul com o driver culpado
e a pilha, achados com evidência e pesos, testes, drivers instalados com versão e data, partidas com FPS e gráficos.
**Pacote para o técnico** gera um .zip com o relatório e os dados brutos (eventos, partidas, testes, correções); os arquivos de tela azul
(.dmp) não vão, porque contêm pedaços da memória do PC.

## FPS e painel no jogo

Durante as partidas, o DiagIT mede **FPS, 1% low (o FPS dos quadros mais lentos) e engasgos** com o
[PresentMon](https://github.com/GameTechDev/PresentMon) da Intel, que lê os eventos de quadros do próprio Windows sem tocar no jogo.
Cada partida guarda o resumo; o diagnóstico aponta jogos que engasgam e o acompanhamento das correções mostra o antes e depois
(*"FPS mínimo em cs2: 42 → 59"*).

O **painel no jogo** (como o do MSI Afterburner) mede o jogo durante a partida e, fora dela, o programa em primeiro plano. Mostra FPS, 1% low, o gráfico do tempo de quadro (engasgos aparecem como picos) e uso e
temperatura de GPU e CPU. Liga sozinho nas partidas (Configurações) e **Ctrl+Shift+F** mostra/esconde a qualquer momento.

## O que mais ele lê

- **Sensores embutidos**: GPU pelo próprio driver de vídeo (AMD ADL / NVIDIA NVML); CPU e placa-mãe pelo LibreHardwareMonitor embutido + driver PawnIO.
  Em placas Gigabyte com chip ITE sem perfil, aplica os divisores de tensão padrão (+12 V = canal 3 × 6).
- **Registros do Windows (30 dias)**: erros WHEA, travamentos do driver de vídeo, LiveKernelEvents, desligamentos sem aviso, telas azuis, erros de disco, limitação de CPU pelo firmware.
- **Arquivos de tela azul** (Minidump e LiveKernelReports): código exato de cada falha e **o driver culpado**, lido da pilha e dos parâmetros
  da falha, junto com o driver que o próprio Windows aponta (evento 1019, disponível mesmo sem administrador). Quando o mesmo driver aparece
  na maioria das telas azuis, o diagnóstico diz qual é (ex.: *driver de vídeo da NVIDIA*) e o que fazer; quando é o núcleo do Windows, aponta
  memória/hardware.
- **Configuração**: pentes de RAM (dual channel, kits misturados), memória virtual, BIOS, driver de vídeo, link PCIe.
- **Partidas gravadas**: se o PC cair no meio do jogo, a partida fica marcada como interrompida com as últimas leituras.
  O app diferencia queda real do PC (Windows reiniciou) de o próprio DiagIT ter sido fechado.
- **Itens do guia da comunidade** ([r/AMDHelp](https://www.reddit.com/r/AMDHelp/comments/1lnxb8o/)) que dá para medir. Cada um só vira achado quando cruza com problemas registrados:
  - o Windows Update trocou o driver de vídeo em uso e os travamentos de vídeo começaram depois;
  - a placa Radeon trabalha acima do clock oficial do modelo (medido nas partidas e no teste de GPU) e há travamentos de vídeo;
  - programas citados como causa de engasgos (Wallpaper Engine, Instant Replay do Adrenalin, softwares de RGB): cada partida registra quais estavam abertos e o diagnóstico compara as partidas com e sem eles; placa de rede Realtek 2.5GbE com congelamentos registrados;
  - arquivo de paginação no HD com o Windows em SSD.

## Correções disponíveis

Memória virtual (tamanho recomendado ou tirar do HD), impedir o Windows Update de trocar drivers (nas atualizações **e** na busca automática de driver, que é a que reinstala um driver antigo logo depois do DDU), SFC + DISM, chkdsk online, teste completo de memória do Windows (no boot),
ativação do registro de telas azuis, instalação limpa do driver de vídeo (passo a passo) e instalação do driver PawnIO.

Na instalação limpa do driver, o DiagIT percebe sozinho quando o driver de vídeo muda com o app aberto (marca o passo *Instale o driver baixado*, atualiza a placa e refaz a análise),
tem o botão *Conferir o driver* (qual está em uso, se veio do Windows Update ou do fabricante, se o Windows ainda pode trocá-lo) e não conta como travamento de GPU os eventos que o próprio DDU e o instalador provocam.
Quando o driver do fabricante entra e o teste da placa passa, o DiagIT registra a troca **na hora em que o driver foi instalado** e passa a acompanhar sozinho:
o que aconteceu antes vira "antes da troca do driver" (informação, sem alerta), o veredito diz que a troca está em acompanhamento e, se o problema voltar,
o alerta volta inteiro e a correção aparece como "ainda não resolveu". As regras de RAM e de disco não contam mais os testes do próprio DiagIT como "pouca RAM" ou "picos de disco".

## Compilar

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

Gera `dist\DiagIT.exe` e copia as bibliotecas de sensores para `dist\sensores`. Use `-OutDir build\dev` para compilar sem
substituir o executável que estiver aberto. Testes sem interface: `DiagIT.exe --selftest saida.txt` e
`DiagIT.exe --run-test cpu|ram|gpu|disk|combined segundos saida.txt`. Cenários do diagnóstico com dados sintéticos: `DiagIT.exe --diag-test saida.txt`. O que o DiagIT extrai de arquivos de tela azul: `DiagIT.exe --dump-info C:\Windows\Minidump saida.txt` (como administrador). Imagens de todas as telas (para revisar o visual): `DiagIT.exe --multi --offscreen --no-elevate --screenshots pasta` (com `--with-run` também fotografa um teste rodando, o que grava esse teste no histórico).

## Regras em dados (contribua sem programar)

O conhecimento do diagnóstico fica em [`data/`](data): códigos de tela azul, textos por fabricante (AMD, NVIDIA, Intel), clock oficial das
placas, programas que causam engasgos e a tradução para linguagem simples. Os arquivos são embutidos no executável; uma pasta `data` ao lado
do `DiagIT.exe` substitui as regras embutidas, para testar uma edição sem compilar. Veja [CONTRIBUTING.md](CONTRIBUTING.md).

## Aviso do Windows ao abrir

O `DiagIT.exe` não tem assinatura digital paga, então o Windows pode mostrar *"O Windows protegeu o computador"* na primeira vez.
Clique em **Mais informações → Executar assim mesmo**. O código está todo aqui e o executável das Releases é compilado a partir dele.

## Dados e privacidade

Tudo fica em `%LOCALAPPDATA%\DiagIT` (sessões, testes, relatórios, log). Nada é enviado para a internet.

## Limitações honestas

- O teste de RAM cobre a memória livre, não a usada pelo Windows; para 100% use o teste de memória do Windows (Resolver).
- A tensão +12 V vem do sensor da placa-mãe (precisão de alguns %): queda é indício forte, mas confirme com multímetro.
- Sem administrador, temperatura da CPU, tensões, SMART e arquivos de tela azul ficam indisponíveis.
- O teste de CPU gera carga comparável a um Cinebench, não ao extremo de um Prime95 com FMA.
- O painel no jogo é uma janela por cima do jogo (não injeta nada, seguro com anti-cheat): aparece em tela cheia sem bordas ou em janela;
  em tela cheia exclusiva o Windows pode escondê-lo.
- Medir FPS precisa do DiagIT aberto como administrador (o PresentMon lê eventos do Windows que exigem isso).

## Licença

[GPL-3.0](LICENSE). Componentes de terceiros e suas licenças: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
