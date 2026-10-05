using Kitopia.Feature.Localization;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Kitopia.Desktop.Features.JsonConverter;
using Kitopia.Desktop.Features.Services.Plugin;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace Kitopia.Desktop.Features.CustomScenario;

public class ScenarioMethod
{
    public string _methodAbsolutelyName;

    public ScenarioMethod()
    {
    }

    public ScenarioMethod(MethodInfo method, PluginLocalInfo pluginInfo, ScenarioMethodAttribute attribute,
        ScenarioMethodType type, IServiceProvider serviceProvider)
    {
        Method = method;
        PluginInfo = pluginInfo;
        Attribute = attribute;
        MethodId = GetMethodId(attribute);
        Type = type;
        ServiceProvider = serviceProvider;
    }

    public ScenarioMethod(ScenarioMethodType type)
    {
        Type = type;
    }


    [JsonIgnore] public IServiceProvider ServiceProvider { get; set; }
    public bool IsFromPlugin => PluginInfo is not null;

    public ScenarioMethodType Type { get; set; }

    //某些特殊的类型需要存储一定的数据，例如（变量读取/设置 需要对应的变量名）
    public string ValueName { get; set; }
    [JsonIgnore] public Type ValueDataType { get; set; }
    [JsonIgnore] public MethodInfo Method { get; set; }
    public PluginLocalInfo? PluginInfo { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MethodId { get; set; }

    [JsonConverter(typeof(ScenarioMethodAttributeJsonCtr))]
    public ScenarioMethodAttribute Attribute { get; set; }

    public string MethodAbsolutelyName
    {
        get
        {
            if (Type == ScenarioMethodType.PluginMethod)
            {
                var sb = new StringBuilder("|");
                var typeJsonConverter = new TypeJsonConverter();

                foreach (var genericArgument in Method.GetParameters())
                {
                    sb.Append(typeJsonConverter.GetTypeName(genericArgument.ParameterType,
                        type => type.Assembly == Method.DeclaringType!.Assembly
                            ? PluginInfo!.PluginBaseInfo
                            : null));
                    sb.Append("|");
                }

                sb.Remove(sb.Length - 1, 1);
                var methodAbsolutelyName =
                    $"{PluginInfo}#{Method.DeclaringType!.FullName}#{Method.Name}{sb}";
                _methodAbsolutelyName = methodAbsolutelyName;
                return methodAbsolutelyName;
            }

            return Type.ToString();
        }
        set => _methodAbsolutelyName = value;
    }


    public string MethodTitle => IsFromPlugin
        ? Attribute.Name
        : Type.ToString();

    internal static string? GetMethodId(ScenarioMethodAttribute? attribute)
    {
        return string.IsNullOrWhiteSpace(attribute?.Id) ? attribute?.Name : attribute.Id;
    }

    internal static bool TryGetReturnValueType(Type returnType, out Type valueType)
    {
        if (returnType == typeof(void) || returnType == typeof(Task) || returnType == typeof(ValueTask))
        {
            valueType = null!;
            return false;
        }

        if (returnType.IsGenericType &&
            (returnType.GetGenericTypeDefinition() == typeof(Task<>) ||
             returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            valueType = returnType.GetGenericArguments()[0];
            return true;
        }

        valueType = returnType;
        return true;
    }

    public ScenarioMethodNode GenerateNode()
    {
        var pointItem = new ScenarioMethodNode
        {
            ScenarioMethod = this,
            Title = MethodTitle
        };
        if (IsFromPlugin)
        {
            ObservableCollection<ConnectorItem> inpItems = new();
            inpItems.Add(new ConnectorItem
            {
                Source = pointItem,
                InputObject = new CustomScenarioValue
                {
                    SerializeType = typeof(NodeConnectorClass)
                },

                Title = "lang.kitopia.stream_input"
            });
            var autoUnboxIndex = 0;
            for (var index = 0;
                 index < Method.GetParameters()
                     .Length;
                 index++)
            {
                var parameterInfo = Method.GetParameters()[index];
                if (parameterInfo.ParameterType == typeof(CancellationToken) ||
                    Nullable.GetUnderlyingType(parameterInfo.ParameterType) == typeof(CancellationToken)) continue;
                var IsSelf = parameterInfo.GetCustomAttributes(typeof(SelfInput))
                    .Any();
                var defaultValue = parameterInfo.HasDefaultValue ? parameterInfo.DefaultValue : null;

                if (parameterInfo.ParameterType.GetCustomAttribute(typeof(AutoUnbox)) is not null)
                {
                    autoUnboxIndex++;
                    var type = parameterInfo.ParameterType;
                    foreach (var memberInfo in type.GetProperties())
                    {
                        if (memberInfo.GetCustomAttribute(typeof(AutoUnboxProperty)) is null) continue;
                        inpItems.Add(new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = memberInfo.PropertyType,
                                IsSelf = IsSelf,
                                Value = defaultValue
                            },

                            AutoUnboxIndex = autoUnboxIndex,
                            AutoUnboxPropertyName = memberInfo.Name,
                            Title = Attribute.GetParameterName(memberInfo.Name)
                        });
                    }
                }
                else
                {
                    var connectorItem = new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = parameterInfo.ParameterType,
                            IsSelf = IsSelf,
                            Value = defaultValue
                        },


                        Title = Attribute.GetParameterName(parameterInfo.Name ?? Lang.Format("lang.kitopia.messages.parameter_value", index + 1))
                    };
                    if (parameterInfo.GetCustomAttribute<CustomNodeInputType>() is not null
                        and var customNodeInputType)
                    {
                        connectorItem.IsPluginInputConnector = true;
                        connectorItem.InputObject.IsSelf = parameterInfo.GetCustomAttribute<SelfInput>() is not null;
                        connectorItem.InputObject.ShowType =customNodeInputType.Type ;
                        connectorItem.PluginInputConnector =
                            (INodeInputConnector)ServiceProvider.GetRequiredService(customNodeInputType.Type);
                    }

                    inpItems.Add(connectorItem);
                }

                //Log.Debug($"参数{index}:类型为{parameterInfo.ParameterType}");
            }

            ObservableCollection<ConnectorItem> outItems = new();
            outItems.Add(new ConnectorItem
            {
                Source = pointItem,
                ConnectorType = ConnectorType.Output,
                InputObject = new CustomScenarioValue
                {
                    SerializeType = typeof(NodeConnectorClass)
                },

                Title = "lang.kitopia.stream_output"
            });
            var returnType = Method.ReturnParameter.ParameterType;
            if (TryGetReturnValueType(returnType, out var returnValueType))
            {
                if (returnValueType.GetCustomAttribute(typeof(AutoUnbox)) is not null)
                {
                    autoUnboxIndex++;
                    var type = returnValueType;
                    foreach (var memberInfo in type.GetProperties())
                    {
                        if (memberInfo.GetCustomAttribute(typeof(AutoUnboxProperty)) is null) continue;
                        outItems.Add(new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = memberInfo.PropertyType
                            },

                            AutoUnboxIndex = autoUnboxIndex,
                            AutoUnboxPropertyName = memberInfo.Name,
                            Title = Attribute.GetParameterName(memberInfo.Name),
                            ConnectorType = ConnectorType.Output
                        });
                    }
                }
                else
                {
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            ShowType = returnValueType,
                            SerializeType = returnValueType
                        },

                        Title = Attribute.GetParameterName("return"),
                        ConnectorType = ConnectorType.Output
                    });
                }
            }

            pointItem.Output = outItems;

            pointItem.Input = inpItems;
        }
        else
        {
            switch (Type)
            {
                case ScenarioMethodType.PluginMethod:
                    break;
                case ScenarioMethodType.Condition:
                {
                    pointItem.Title = "lang.kitopia.condition";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },

                            Title = "lang.kitopia.true",
                            ConnectorType = ConnectorType.Output
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },
                            Title = "lang.kitopia.false",
                            ConnectorType = ConnectorType.Output
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },
                            Title = "lang.kitopia.stream_input"
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(bool)
                            },
                            Title = CustomScenarioGlobe.GetTypeNameKey(typeof(bool).FullName)
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.OneToTwo:
                {
                    pointItem.Title = "lang.kitopia.split_into_two_outputs";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },

                            Title = "lang.kitopia.stream_output",
                            ConnectorType = ConnectorType.Output
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },
                            ConnectorType = ConnectorType.Output,
                            Title = "lang.kitopia.stream_output"
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },
                            Title = "lang.kitopia.stream_input"
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.OneToMany:
                {
                    pointItem.Title = "lang.kitopia.split_into_multiple_outputs";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },
                            Title = "lang.kitopia.stream_output",
                            ConnectorType = ConnectorType.Output
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },
                            ConnectorType = ConnectorType.Output,
                            Title = "lang.kitopia.stream_output"
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },
                            Title = "lang.kitopia.stream_input"
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(int),
                                Value = 2,
                                IsSelf = true
                            },
                            OnlySelfInput = true,
                            Title = "lang.kitopia.output_count"
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.Equal:
                {
                    pointItem.Title = "lang.kitopia.equal";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(bool)
                            },
                            Title = CustomScenarioGlobe.GetTypeNameKey(typeof(bool).FullName),
                            ConnectorType = ConnectorType.Output
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },
                            Title = "lang.kitopia.stream_input"
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(object)
                            },
                            Title = CustomScenarioGlobe.GetTypeNameKey(typeof(object).FullName)
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(object)
                            },
                            Title = CustomScenarioGlobe.GetTypeNameKey(typeof(object).FullName)
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.VariableSet:
                {
                    pointItem.Title = $"{ValueName}";
                    ObservableCollection<ConnectorItem> inpItems = new();
                    inpItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },

                        Title = "lang.kitopia.stream_input"
                    });
                    inpItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = ValueDataType
                        },

                        Title = "lang.kitopia.settings"
                    });
                    pointItem.Input = inpItems;
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        ConnectorType = ConnectorType.Output,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },
                        Title = "lang.kitopia.stream_output"
                    });
                    pointItem.Output = outItems;
                    break;
                }
                case ScenarioMethodType.VariableGet:
                {
                    pointItem.Title = $"{ValueName}";
                    ObservableCollection<ConnectorItem> inpItems = new();
                    inpItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },
                        Title = "lang.kitopia.stream_input"
                    });
                    pointItem.Input = inpItems;
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },
                        ConnectorType = ConnectorType.Output,
                        Title = "lang.kitopia.stream_output"
                    });
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = ValueDataType
                        },

                        Title = "lang.kitopia.get",
                        ConnectorType = ConnectorType.Output
                    });
                    pointItem.Output = outItems;
                    break;
                }
                case ScenarioMethodType.TempVariableSet:
                {
                    pointItem.Title = $"{ValueName}";
                    ObservableCollection<ConnectorItem> inpItems = new();
                    inpItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },

                        Title = "lang.kitopia.stream_input"
                    });
                    inpItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(object)
                        },

                        Title = "lang.kitopia.settings"
                    });
                    pointItem.Input = inpItems;
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        ConnectorType = ConnectorType.Output,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },
                        Title = "lang.kitopia.stream_output"
                    });
                    pointItem.Output = outItems;
                    break;
                }
                case ScenarioMethodType.TempVariableGet:
                {
                    pointItem.Title = $"{ValueName}";
                    ObservableCollection<ConnectorItem> inpItems = new();
                    inpItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },
                        Title = "lang.kitopia.stream_input"
                    });
                    pointItem.Input = inpItems;
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },
                        ConnectorType = ConnectorType.Output,
                        Title = "lang.kitopia.stream_output"
                    });
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(object)
                        },

                        Title = "lang.kitopia.get",
                        ConnectorType = ConnectorType.Output
                    });
                    pointItem.Output = outItems;
                    break;
                }
                case ScenarioMethodType.InputVariableGet:
                {
                    pointItem.Title = $"{ValueName}";
                    ObservableCollection<ConnectorItem> inpItems = new();
                    inpItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },
                        Title = "lang.kitopia.stream_input"
                    });
                    pointItem.Input = inpItems;
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },
                        ConnectorType = ConnectorType.Output,
                        Title = "lang.kitopia.stream_output"
                    });
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = ValueDataType
                        },

                        Title = "lang.kitopia.get",
                        ConnectorType = ConnectorType.Output
                    });
                    pointItem.Output = outItems;
                    break;
                }
                case ScenarioMethodType.OpenRunLocalProject:
                {
                    pointItem.Title = "lang.kitopia.open_or_run_local_item";
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        ConnectorType = ConnectorType.Output,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },
                        Title = "lang.kitopia.stream_output"
                    });
                    pointItem.Output = outItems;
                    ObservableCollection<ConnectorItem> pointInItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },
                            Title = "lang.kitopia.stream_input"
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(string),
                                ShowType = typeof(SearchViewItem),
                                Value = "",
                                IsSelf = true
                            },


                            Title = "lang.kitopia.local_item"
                        }
                    };
                    pointItem.Input = pointInItems;
                    break;
                }
            }
        }


        return pointItem;
    }
}
