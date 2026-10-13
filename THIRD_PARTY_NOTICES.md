# Componentes de terceiros

## LibreHardwareMonitorLib 0.9.6 (e dependências em vendor\lhm)
- Projeto: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- Licença: Mozilla Public License 2.0 (https://www.mozilla.org/MPL/2.0/)
- Usado sem modificações, apenas para ler sensores de CPU e placa-mãe. O código-fonte está disponível no link acima.
- As demais DLLs (System.*, Microsoft.Bcl.*, HidSharp, DiskInfoToolkit, RAMSPDToolkit-NDD, BlackSharp.Core) são dependências
  distribuídas junto com o LibreHardwareMonitor, cada uma sob sua própria licença (MIT / Apache-2.0).

## PawnIO 2.2.0 (driver, instalado separadamente)
- Projeto: https://pawnio.eu/ e https://github.com/namazso/PawnIO.Setup
- Driver de kernel assinado usado pelo LibreHardwareMonitor para acesso de baixo nível (temperatura da CPU, chip da placa-mãe).
- Não é distribuído junto com o DiagIT; instale com:  winget install namazso.PawnIO

## PresentMon 2.5.1 (pasta PresentMon)
- Projeto: https://github.com/GameTechDev/PresentMon (Intel Corporation)
- Licença: MIT (texto completo em `vendor/presentmon/LICENSE.txt`, copiado para a pasta `PresentMon` do pacote)
- Executável oficial da release, assinado pela Intel, usado sem modificações para medir FPS, 1% low e engasgos dos jogos.
  Ele lê os eventos de apresentação de quadros do Windows (ETW); não injeta nada no jogo.
