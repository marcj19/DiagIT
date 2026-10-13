# DiagIT — diagnóstico de hardware com testes comprovados

Aplicativo para Windows que **monitora as partidas em segundo plano**, **testa cada peça do PC sob carga com verificação de
resultado** e **aponta o componente com problema comprovado**, junto com o que fazer para resolver.

Feito em C# / Windows Forms (.NET Framework 4.8, que já vem no Windows). Não precisa do HWiNFO nem de outro programa aberto.

## Como usar

1. Abra `dist\DiagIT.exe` (ele pede permissão de administrador — necessária para ler a CPU, a placa-mãe, o SMART dos discos e os arquivos de tela azul).
2. **Jogue normalmente.** O DiagIT fica na bandeja, detecta o jogo sozinho, grava a telemetria da partida e avisa no fim se viu algo errado.
3. **Aba Testes** (com os jogos fechados): rode a *Bateria completa* (~20 min) ou testes individuais.
4. **Aba Diagnóstico**: cada componente aparece como **PROBLEMA** (comprovado), **SUSPEITO** (há indícios — o teste indicado confirma ou descarta), **OK** (passou no teste) ou **NÃO TESTADO**.
5. **Aba Correções**: aplica as soluções com um clique (sempre pedindo confirmação).

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

## O que mais ele lê

- **Sensores embutidos**: GPU pelo próprio driver de vídeo (AMD ADL / NVIDIA NVML); CPU e placa-mãe pelo LibreHardwareMonitor embutido + driver PawnIO.
  Em placas Gigabyte com chip ITE sem perfil, aplica os divisores de tensão padrão (+12 V = canal 3 × 6).
- **Registros do Windows (30 dias)**: erros WHEA, travamentos do driver de vídeo, LiveKernelEvents, desligamentos sem aviso, telas azuis, erros de disco, limitação de CPU pelo firmware.
- **Arquivos de tela azul** (Minidump e LiveKernelReports): código exato de cada falha.
- **Configuração**: pentes de RAM (dual channel, kits misturados), memória virtual, BIOS, driver de vídeo, link PCIe.
- **Partidas gravadas**: se o PC cair no meio do jogo, a partida fica marcada como interrompida com as últimas leituras.
  O app diferencia queda real do PC (Windows reiniciou) de o próprio DiagIT ter sido fechado.

## Correções disponíveis

Memória virtual (tamanho recomendado), SFC + DISM, chkdsk online, teste completo de memória do Windows (no boot),
ativação do registro de telas azuis, instalação limpa do driver de vídeo (passo a passo) e instalação do driver PawnIO.

## Compilar

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

Gera `dist\DiagIT.exe` e copia as bibliotecas de sensores para `dist\sensores`. Use `-OutDir build\dev` para compilar sem
substituir o executável que estiver aberto. Testes sem interface: `DiagIT.exe --selftest saida.txt` e
`DiagIT.exe --run-test cpu|ram|gpu|disk|combined segundos saida.txt`.

## Dados e privacidade

Tudo fica em `%LOCALAPPDATA%\DiagIT` (sessões, testes, relatórios, log). Nada é enviado para a internet.

## Limitações honestas

- O teste de RAM cobre a memória livre, não a usada pelo Windows; para 100% use o teste do Windows (aba Correções).
- A tensão +12 V vem do sensor da placa-mãe (precisão de alguns %): queda é indício forte, mas confirme com multímetro.
- Sem administrador, temperatura da CPU, tensões, SMART e arquivos de tela azul ficam indisponíveis.
- O teste de CPU gera carga comparável a um Cinebench (no Ryzen 5 5500: ~56 W), não ao extremo de um Prime95 com FMA.
- Não mede FPS/frametime.

Componentes de terceiros: veja `THIRD_PARTY_NOTICES.md`.
