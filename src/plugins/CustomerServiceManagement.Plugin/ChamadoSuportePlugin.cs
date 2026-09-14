using Microsoft.Xrm.Sdk;
using System;

namespace CustomerServiceManagement.Plugin
{
    /// <summary>
    /// Plugin para a entidade db_chamadodesuporte.
    /// Registrado no evento: Create (Pre-Operation - Stage 20).
    /// 
    /// Regras de Negócio Server-Side:
    /// 1. Geração automática de protocolo único (ATD-yyyyMMdd-XXXX) se não preenchido.
    /// 2. Preenchimento de Data de Abertura com data/hora UTC atual se nula.
    /// 3. Validação: Chamados críticos exigem descrição detalhada do problema (mínimo de 10 caracteres).
    /// </summary>
    public class ChamadoSuportePlugin : PluginBase
    {
        public ChamadoSuportePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ChamadoSuportePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;

            // Garantir que estamos no estágio de execução correto com parâmetro Target
            if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity target)
            {
                if (target.LogicalName != "db_chamadodesuporte")
                {
                    return;
                }

                localPluginContext.Trace($"Executando ChamadoSuportePlugin para a entidade {target.LogicalName} [Stage: {context.Stage}]");

                // 1. Geração Automática do Número de Protocolo
                if (!target.Contains("db_numerodoprotocolo") || string.IsNullOrWhiteSpace(target.GetAttributeValue<string>("db_numerodoprotocolo")))
                {
                    var dataAtual = DateTime.UtcNow.ToString("yyyyMMdd");
                    var aleatorio = new Random().Next(1000, 9999);
                    var novoProtocolo = $"ATD-{dataAtual}-{aleatorio}";

                    target["db_numerodoprotocolo"] = novoProtocolo;
                    localPluginContext.Trace($"Protocolo gerado com sucesso: {novoProtocolo}");
                }

                // 2. Preenchimento de Data de Abertura se ausente
                if (!target.Contains("db_datadeabertura") || target.GetAttributeValue<DateTime?>("db_datadeabertura") == null)
                {
                    target["db_datadeabertura"] = DateTime.UtcNow;
                    localPluginContext.Trace("Data de abertura preenchida com DateTime.UtcNow.");
                }

                // 3. Validação de Negócio: Chamados Críticos exigem Descrição
                if (target.Contains("db_prioridadedochamado"))
                {
                    var prioridadeTexto = target.GetAttributeValue<string>("db_prioridadedochamado");
                    var prioridadeOpcao = target.GetAttributeValue<OptionSetValue>("db_prioridadedochamado");

                    bool isCritica = false;
                    if (!string.IsNullOrEmpty(prioridadeTexto) && 
                        (prioridadeTexto.IndexOf("Critica", StringComparison.OrdinalIgnoreCase) >= 0 || 
                         prioridadeTexto.IndexOf("Crítica", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        isCritica = true;
                    }

                    if (isCritica)
                    {
                        var descricao = target.Contains("db_descricaodoproblema") 
                            ? target.GetAttributeValue<string>("db_descricaodoproblema") 
                            : string.Empty;

                        if (string.IsNullOrWhiteSpace(descricao) || descricao.Trim().Length < 10)
                        {
                            throw new InvalidPluginExecutionException(
                                "Regra de Negócio: Chamados com prioridade Crítica exigem uma descrição detalhada do problema (mínimo 10 caracteres)."
                            );
                        }
                    }
                }
            }
        }
    }
}
