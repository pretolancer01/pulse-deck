# PulseDeck — componentes de terceiros

O executável inclui o runtime .NET e bibliotecas de terceiros. O projeto mantém os pacotes fixados em `src/PulseDeck/packages.lock.json`.

- .NET / WPF / Windows Desktop Runtime, Microsoft — MIT e avisos próprios: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT e https://github.com/dotnet/wpf/blob/main/LICENSE.TXT
- System.Management 10.0.2, Microsoft — MIT: https://github.com/dotnet/runtime
- LibreHardwareMonitorLib 0.9.6 — MPL-2.0. Código-fonte original sem alterações: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701 ; licença: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/Licenses/LICENSE
- HidSharp 2.6.4 — licença e copyright originais: https://www.zer7.com/software/hidsharp
- DiskInfoToolkit 1.1.2 — licença e fonte: https://www.nuget.org/packages/DiskInfoToolkit/1.1.2
- RAMSPDToolkit-NDD 1.4.2 — licença e fonte: https://www.nuget.org/packages/RAMSPDToolkit-NDD/1.4.2
- Demais dependências transitivas e suas licenças estão identificadas nos metadados NuGet e no arquivo de dependências fixadas. O build não altera o código dessas bibliotecas.

A integração AMD usa a DLL ADLX já instalada com o driver AMD, carregada pelo caminho absoluto do Windows. A DLL AMD não é redistribuída. As assinaturas públicas da API foram consultadas no SDK oficial, revisão `32b5a740d42295c5dfe9026b9f52683da0f3af91`: https://github.com/GPUOpen-LibrariesAndSDKs/ADLX . Os termos do SDK estão no repositório oficial.

A temperatura Ryzen usa a API pública de leitura do AMD Ryzen Master Monitoring SDK 3.1.1.5478 quando instalado no PC. As DLLs e o driver da AMD não são incorporados ao PulseDeck; a ponte nativa `PulseDeckRyzenBridge.dll` é código próprio e está embutida no EXE. Download e termos do SDK: https://www.amd.com/en/developer/ryzen-master-monitoring-sdk.html .

PawnIO não é distribuído nem instalado pelo PulseDeck. O diagnóstico consulta sua presença. Projeto oficial: https://github.com/namazso/PawnIO.Setup .

Referência de processo de design consultada a pedido do usuário: Hallmark, Nutlope — MIT: https://github.com/Nutlope/hallmark . As diretrizes foram adaptadas ao aplicativo WPF; nenhum runtime web do Hallmark é incorporado. A marca de pulso, ícone, geometrias 3D e introdução do PulseDeck são originais; não usam marcas nem arquivos da Riot.
