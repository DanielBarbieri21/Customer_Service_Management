using Microsoft.Crm.Sdk.Messages;
using Microsoft.Identity.Client;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

const string EnvUrl = "https://orga811bd74.crm2.dynamics.com";
const string SolutionName = "CustomerServiceManagement";
const string WellKnownClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";
const int OptionPrefix = 59571;

var pca = PublicClientApplicationBuilder
    .Create(WellKnownClientId)
    .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
    .WithRedirectUri("http://localhost")
    .Build();

async Task<string> GetToken(string instanceUrl)
{
    var scopes = new[] { $"{instanceUrl.TrimEnd('/')}/user_impersonation" };
    var account = (await pca.GetAccountsAsync()).FirstOrDefault();
    AuthenticationResult result;
    try
    {
        if (account is null)
        {
            throw new MsalUiRequiredException("no_account", "Nenhuma conta em cache.");
        }

        result = await pca.AcquireTokenSilent(scopes, account).ExecuteAsync();
    }
    catch (MsalException)
    {
        result = await pca.AcquireTokenWithDeviceCode(scopes, info =>
        {
            Console.WriteLine(info.Message);
            return Task.CompletedTask;
        }).ExecuteAsync();
    }

    return result.AccessToken;
}

using var service = new ServiceClient(new Uri(EnvUrl), GetToken);
if (!service.IsReady)
{
    throw new InvalidOperationException(service.LastError);
}

Console.WriteLine($"Conectado: {service.ConnectedOrgFriendlyName}");

var org = service.RetrieveMultiple(new QueryExpression("organization")
{
    ColumnSet = new ColumnSet("languagecode"),
    TopCount = 1
}).Entities.First();
var lang = org.GetAttributeValue<int>("languagecode");
Console.WriteLine($"Idioma da organização: {lang}");

Label L(string text) => new(text, lang);

AttributeRequiredLevelManagedProperty Required(AttributeRequiredLevel level) =>
    new(level);

void AddToSolution(Guid id, int componentType)
{
    try
    {
        service.Execute(new AddSolutionComponentRequest
        {
            ComponentId = id,
            ComponentType = componentType,
            SolutionUniqueName = SolutionName,
            AddRequiredComponents = false,
            DoNotIncludeSubcomponents = false
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  (componente já na solution ou aviso: {ex.Message})");
    }
}

bool OptionSetExists(string name)
{
    try
    {
        service.Execute(new RetrieveOptionSetRequest { Name = name });
        return true;
    }
    catch
    {
        return false;
    }
}

bool TableExists(string logicalName)
{
    try
    {
        service.Execute(new RetrieveEntityRequest
        {
            LogicalName = logicalName,
            EntityFilters = EntityFilters.Entity
        });
        return true;
    }
    catch
    {
        return false;
    }
}

bool AttributeExists(string entity, string attribute)
{
    try
    {
        service.Execute(new RetrieveAttributeRequest
        {
            EntityLogicalName = entity,
            LogicalName = attribute
        });
        return true;
    }
    catch
    {
        return false;
    }
}

bool RelationshipExists(string schemaName)
{
    try
    {
        service.Execute(new RetrieveRelationshipRequest { Name = schemaName });
        return true;
    }
    catch
    {
        return false;
    }
}

Guid CreateGlobalChoice(string name, string display, (int Value, string Label)[] options)
{
    if (OptionSetExists(name))
    {
        Console.WriteLine($"Choice já existe: {name}");
        var existing = (RetrieveOptionSetResponse)service.Execute(new RetrieveOptionSetRequest { Name = name });
        AddToSolution(existing.OptionSetMetadata.MetadataId!.Value, 9);
        return existing.OptionSetMetadata.MetadataId.Value;
    }

    var optionSet = new OptionSetMetadata
    {
        Name = name,
        DisplayName = L(display),
        Description = L(display),
        IsGlobal = true,
        OptionSetType = OptionSetType.Picklist
    };
    foreach (var (value, label) in options)
    {
        optionSet.Options.Add(new OptionMetadata(L(label), value));
    }

    var response = (CreateOptionSetResponse)service.Execute(new CreateOptionSetRequest { OptionSet = optionSet });
    Console.WriteLine($"Choice criada: {name}");
    AddToSolution(response.OptionSetId, 9);
    return response.OptionSetId;
}

Guid CreateTable(string schema, string display, string plural, string description, string primarySchema, string primaryDisplay, int primaryLength, bool notes, bool activities)
{
    var logical = schema.ToLowerInvariant();
    if (TableExists(logical))
    {
        Console.WriteLine($"Tabela já existe: {schema}");
        var existing = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
        {
            LogicalName = logical,
            EntityFilters = EntityFilters.Entity
        });
        AddToSolution(existing.EntityMetadata.MetadataId!.Value, 1);
        return existing.EntityMetadata.MetadataId.Value;
    }

    var entity = new EntityMetadata
    {
        SchemaName = schema,
        DisplayName = L(display),
        DisplayCollectionName = L(plural),
        Description = L(description),
        OwnershipType = OwnershipTypes.UserOwned,
        IsActivity = false,
        HasNotes = notes,
        HasActivities = activities
    };

    var primary = new StringAttributeMetadata
    {
        SchemaName = primarySchema,
        RequiredLevel = Required(AttributeRequiredLevel.ApplicationRequired),
        MaxLength = primaryLength,
        DisplayName = L(primaryDisplay),
        Description = L(primaryDisplay),
        FormatName = StringFormatName.Text
    };

    var response = (CreateEntityResponse)service.Execute(new CreateEntityRequest
    {
        Entity = entity,
        PrimaryAttribute = primary,
        HasActivities = activities,
        HasNotes = notes
    });
    Console.WriteLine($"Tabela criada: {schema}");
    AddToSolution(response.EntityId, 1);
    return response.EntityId;
}

void CreateString(string entity, string schema, string display, int max, AttributeRequiredLevel required, StringFormatName? format)
{
    var logical = schema.ToLowerInvariant();
    if (AttributeExists(entity, logical))
    {
        Console.WriteLine($"  Campo já existe: {schema}");
        return;
    }

    var attr = new StringAttributeMetadata
    {
        SchemaName = schema,
        DisplayName = L(display),
        Description = L(display),
        RequiredLevel = Required(required),
        MaxLength = max,
        FormatName = format ?? StringFormatName.Text
    };
    service.Execute(new CreateAttributeRequest { EntityName = entity, Attribute = attr });
    Console.WriteLine($"  Campo texto: {schema}");
}

void CreateMemo(string entity, string schema, string display, int max)
{
    var logical = schema.ToLowerInvariant();
    if (AttributeExists(entity, logical))
    {
        Console.WriteLine($"  Campo já existe: {schema}");
        return;
    }

    var attr = new MemoAttributeMetadata
    {
        SchemaName = schema,
        DisplayName = L(display),
        Description = L(display),
        RequiredLevel = Required(AttributeRequiredLevel.None),
        MaxLength = max
    };
    service.Execute(new CreateAttributeRequest { EntityName = entity, Attribute = attr });
    Console.WriteLine($"  Campo texto longo: {schema}");
}

void CreateDate(string entity, string schema, string display, bool dateOnly, AttributeRequiredLevel required)
{
    var logical = schema.ToLowerInvariant();
    if (AttributeExists(entity, logical))
    {
        Console.WriteLine($"  Campo já existe: {schema}");
        return;
    }

    var attr = new DateTimeAttributeMetadata
    {
        SchemaName = schema,
        DisplayName = L(display),
        Description = L(display),
        RequiredLevel = Required(required),
        Format = dateOnly ? DateTimeFormat.DateOnly : DateTimeFormat.DateAndTime,
        DateTimeBehavior = dateOnly ? DateTimeBehavior.DateOnly : DateTimeBehavior.UserLocal
    };
    service.Execute(new CreateAttributeRequest { EntityName = entity, Attribute = attr });
    Console.WriteLine($"  Campo data: {schema}");
}

void CreateChoiceColumn(string entity, string schema, string display, string globalName, int defaultValue, AttributeRequiredLevel required)
{
    var logical = schema.ToLowerInvariant();
    if (AttributeExists(entity, logical))
    {
        Console.WriteLine($"  Campo já existe: {schema}");
        return;
    }

    var attr = new PicklistAttributeMetadata
    {
        SchemaName = schema,
        DisplayName = L(display),
        Description = L(display),
        RequiredLevel = Required(required),
        DefaultFormValue = defaultValue,
        OptionSet = new OptionSetMetadata
        {
            IsGlobal = true,
            Name = globalName
        }
    };
    service.Execute(new CreateAttributeRequest { EntityName = entity, Attribute = attr });
    Console.WriteLine($"  Campo choice: {schema}");
}

void CreateLookup(string schema, string referenced, string referencing, string lookupSchema, string lookupDisplay, bool required, CascadeType delete)
{
    if (RelationshipExists(schema.ToLowerInvariant()) || RelationshipExists(schema))
    {
        Console.WriteLine($"Relacionamento já existe: {schema}");
        return;
    }

    var request = new CreateOneToManyRequest
    {
        OneToManyRelationship = new OneToManyRelationshipMetadata
        {
            SchemaName = schema,
            ReferencedEntity = referenced,
            ReferencingEntity = referencing,
            AssociatedMenuConfiguration = new AssociatedMenuConfiguration
            {
                Behavior = AssociatedMenuBehavior.UseCollectionName,
                Group = AssociatedMenuGroup.Details,
                Order = 10000
            },
            CascadeConfiguration = new CascadeConfiguration
            {
                Assign = CascadeType.NoCascade,
                Delete = delete,
                Merge = CascadeType.NoCascade,
                Reparent = CascadeType.NoCascade,
                Share = CascadeType.NoCascade,
                Unshare = CascadeType.NoCascade
            }
        },
        Lookup = new LookupAttributeMetadata
        {
            SchemaName = lookupSchema,
            DisplayName = L(lookupDisplay),
            Description = L(lookupDisplay),
            RequiredLevel = Required(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None)
        }
    };

    var response = (CreateOneToManyResponse)service.Execute(request);
    Console.WriteLine($"Relacionamento criado: {schema}");
    AddToSolution(response.RelationshipId, 10);
}

Console.WriteLine("=== Choices ===");
CreateGlobalChoice("db_statuscliente", "Status do Cliente",
[
    (OptionPrefix * 10000 + 0, "Ativo"),
    (OptionPrefix * 10000 + 1, "Inativo"),
    (OptionPrefix * 10000 + 2, "Prospect")
]);
CreateGlobalChoice("db_prioridade", "Prioridade",
[
    (OptionPrefix * 10000 + 10, "Baixa"),
    (OptionPrefix * 10000 + 11, "Média"),
    (OptionPrefix * 10000 + 12, "Alta"),
    (OptionPrefix * 10000 + 13, "Crítica")
]);
CreateGlobalChoice("db_statusatendimento", "Status do Atendimento",
[
    (OptionPrefix * 10000 + 20, "Aberto"),
    (OptionPrefix * 10000 + 21, "Em andamento"),
    (OptionPrefix * 10000 + 22, "Aguardando cliente"),
    (OptionPrefix * 10000 + 23, "Resolvido"),
    (OptionPrefix * 10000 + 24, "Cancelado")
]);
CreateGlobalChoice("db_canal", "Canal de Atendimento",
[
    (OptionPrefix * 10000 + 30, "Telefone"),
    (OptionPrefix * 10000 + 31, "E-mail"),
    (OptionPrefix * 10000 + 32, "Chat"),
    (OptionPrefix * 10000 + 33, "Presencial"),
    (OptionPrefix * 10000 + 34, "Portal")
]);
CreateGlobalChoice("db_statustarefa", "Status da Tarefa",
[
    (OptionPrefix * 10000 + 40, "Pendente"),
    (OptionPrefix * 10000 + 41, "Em andamento"),
    (OptionPrefix * 10000 + 42, "Concluída"),
    (OptionPrefix * 10000 + 43, "Cancelada")
]);

var ativo = OptionPrefix * 10000 + 0;
var baixa = OptionPrefix * 10000 + 10;
var aberto = OptionPrefix * 10000 + 20;
var pendente = OptionPrefix * 10000 + 40;

Console.WriteLine("=== Tabelas ===");
CreateTable("db_cliente", "Cliente", "Clientes",
    "Cadastro de clientes atendidos pela equipe.",
    "db_nome", "Nome", 200, notes: true, activities: true);
CreateString("db_cliente", "db_email", "E-mail", 100, AttributeRequiredLevel.None, StringFormatName.Email);
CreateString("db_cliente", "db_telefone", "Telefone", 20, AttributeRequiredLevel.None, StringFormatName.Phone);
CreateString("db_cliente", "db_cpf", "CPF", 14, AttributeRequiredLevel.None, StringFormatName.Text);
CreateString("db_cliente", "db_empresa", "Empresa", 200, AttributeRequiredLevel.None, StringFormatName.Text);
CreateChoiceColumn("db_cliente", "db_statuscliente", "Status", "db_statuscliente", ativo, AttributeRequiredLevel.ApplicationRequired);
CreateDate("db_cliente", "db_datanascimento", "Data de nascimento", dateOnly: true, AttributeRequiredLevel.None);
CreateMemo("db_cliente", "db_observacoes", "Observações", 2000);

CreateTable("db_atendimento", "Atendimento", "Atendimentos",
    "Registro de chamados e atendimentos de cliente.",
    "db_titulo", "Título", 200, notes: true, activities: true);
CreateString("db_atendimento", "db_protocolo", "Protocolo", 20, AttributeRequiredLevel.None, StringFormatName.Text);
CreateMemo("db_atendimento", "db_descricao", "Descrição", 4000);
CreateChoiceColumn("db_atendimento", "db_prioridade", "Prioridade", "db_prioridade", baixa, AttributeRequiredLevel.ApplicationRequired);
CreateChoiceColumn("db_atendimento", "db_statusatendimento", "Status", "db_statusatendimento", aberto, AttributeRequiredLevel.ApplicationRequired);
CreateChoiceColumn("db_atendimento", "db_canal", "Canal", "db_canal", OptionPrefix * 10000 + 30, AttributeRequiredLevel.None);
CreateDate("db_atendimento", "db_dataabertura", "Data de abertura", dateOnly: false, AttributeRequiredLevel.ApplicationRequired);
CreateDate("db_atendimento", "db_datafechamento", "Data de fechamento", dateOnly: false, AttributeRequiredLevel.None);

CreateTable("db_tarefa", "Tarefa", "Tarefas",
    "Tarefas vinculadas a um atendimento.",
    "db_assunto", "Assunto", 200, notes: true, activities: false);
CreateMemo("db_tarefa", "db_descricao", "Descrição", 2000);
CreateChoiceColumn("db_tarefa", "db_statustarefa", "Status", "db_statustarefa", pendente, AttributeRequiredLevel.ApplicationRequired);
CreateDate("db_tarefa", "db_dataprevisao", "Previsão", dateOnly: true, AttributeRequiredLevel.None);
CreateDate("db_tarefa", "db_dataconclusao", "Data de conclusão", dateOnly: false, AttributeRequiredLevel.None);

Console.WriteLine("=== Relacionamentos ===");
CreateLookup("db_cliente_db_atendimento", "db_cliente", "db_atendimento",
    "db_clienteid", "Cliente", required: true, CascadeType.Restrict);
CreateLookup("db_atendimento_db_tarefa", "db_atendimento", "db_tarefa",
    "db_atendimentoid", "Atendimento", required: true, CascadeType.Cascade);

service.Execute(new PublishAllXmlRequest());
Console.WriteLine("Publicação concluída.");
