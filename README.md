# PulseDeck

Aplicativo nativo de monitoramento e ajustes para Windows x64, criado em C# / WPF / .NET 10.

## Baixar e usar

Baixe `PulseDeck.exe` em **Releases** e execute-o. O runtime .NET está incluído. Ao clicar no X, a janela vai para os ícones ocultos e o monitoramento continua; clique duas vezes no ícone para reabrir ou use **Sair do PulseDeck** no menu do ícone para encerrar e restaurar os ajustes da sessão.

O Windows pode exibir um aviso porque o executável ainda não é assinado. Examine o código e compile por conta própria se preferir. O aplicativo lê os sensores disponíveis em cada máquina; valores e controles ausentes ficam indisponíveis.

## Recursos

- Painel com métricas de CPU, GPU, RAM e processos, histórico animado e gráfico 3D interativo por aplicativo.
- Perfis de ventoinha da GPU quando o driver AMD ADLX expõe o controle; sensores da placa-mãe aparecem quando detectados.
- Ajustes de frequência da GPU apenas nos limites informados pelo driver, com confirmação, supervisão térmica e restauração ao sair.
- Presets reversíveis de preferências do Windows, com opção **Padrão do Windows** e restauração da configuração anterior.
- Overlay movível com CPU, RAM, GPU, temperatura e FPS quando as fontes correspondentes estão disponíveis.
- Limpeza de arquivos temporários com revisão antes de enviar à Lixeira.
- Animações, atalhos de teclado e opção de reduzir movimento.

## Fontes opcionais de sensores

- GPU AMD: requer driver com ADLX. Outras GPUs podem ter métricas limitadas e não recebem controles AMD.
- Temperatura Ryzen: pode usar o AMD Ryzen Master Monitoring SDK instalado separadamente; seus binários não acompanham o PulseDeck.
- Placa-mãe e ventoinhas: LibreHardwareMonitor. A disponibilidade de sensores e controles depende do hardware, driver e permissões.
- FPS: instale o PresentMon separadamente e defina `PULSEDECK_PRESENTMON_PATH` para o caminho completo do executável, ou coloque `PresentMon.exe` ao lado do PulseDeck.

O PulseDeck não inventa valores quando a fonte falha. Ajustes de hardware têm riscos de instabilidade e podem não ser revertidos após encerramento forçado ou falha do sistema. A CPU não recebe comandos de overclock: o SDK público de monitoramento é somente leitura.

## Compilar

Instale o SDK .NET 10 para Windows e execute:

```powershell
.\build.ps1
```

O script publica um EXE independente em `dist/PulseDeck.exe`. O código principal está em `src/PulseDeck/`. A ponte nativa de leitura Ryzen tem fonte em `src/PulseDeck/Native/`; o binário correspondente está incluído no projeto. Dependências NuGet ficam fixadas em `packages.lock.json`. Para rodar verificações locais:

```powershell
.\dist\PulseDeck.exe --self-test --output .\hardware-report.json
.\dist\PulseDeck.exe --ui-test --output .\ui-test-results.txt
.\dist\PulseDeck.exe --tray-test --output .\tray-test.txt
```

Os relatórios de diagnóstico contêm dados da máquina e não devem ser publicados. Preferências e logs ficam em `%LOCALAPPDATA%\PulseDeck`. Consulte [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) e [THIRD-PARTY-LICENSES.txt](THIRD-PARTY-LICENSES.txt) para licenças de terceiros.

## Licença

O código original do PulseDeck é disponibilizado sob a licença MIT em [LICENSE](LICENSE). Componentes de terceiros preservam suas próprias licenças.

