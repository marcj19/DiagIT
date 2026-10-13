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
