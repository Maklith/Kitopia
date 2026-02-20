using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Core.JsonConverter;
using Core.Services.Plugin;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Attribute.Scenario;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace Core.CustomScenario;

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
                    sb.Append(typeJsonConverter.GetTypeName(genericArgument.ParameterType));
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

                Title = "流输入"
            });
            var autoUnboxIndex = 0;
            for (var index = 0;
                 index < Method.GetParameters()
                     .Length;
                 index++)
            {
                var parameterInfo = Method.GetParameters()[index];
                if (parameterInfo.ParameterType.FullName == "System.Threading.CancellationToken") continue;
                if (parameterInfo.ParameterType.FullName.StartsWith(
                        "System.Nullable`1[[System.Threading.CancellationToken,")) continue;
                var IsSelf = parameterInfo.GetCustomAttributes(typeof(SelfInput))
                    .Any();
                var defaultValue = parameterInfo.DefaultValue;

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


                        Title = Attribute.GetParameterName(parameterInfo.Name)
                    };
                    if (parameterInfo.GetCustomAttribute<CustomNodeInputType>() is not null
                        and var customNodeInputType)
                    {
                        connectorItem.isPluginInputConnector = true;
                        connectorItem.InputObject.IsSelf = parameterInfo.GetCustomAttribute<SelfInput>() is not null;
                        connectorItem.InputObject.ShowType =customNodeInputType.Type ;
                        try
                        {
                            var service = ServiceProvider.GetService(customNodeInputType.Type);
                            connectorItem.PluginInputConnector = service as INodeInputConnector;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                            throw;
                        }
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

                Title = "流输出"
            });
            if (Method.ReturnParameter.ParameterType != typeof(void))
            {
                if (Method.ReturnParameter.ParameterType.GetCustomAttribute(typeof(AutoUnbox)) is not null)
                {
                    autoUnboxIndex++;
                    var type = Method.ReturnParameter.ParameterType;
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
                   
                    var type = Method.ReturnParameter.ParameterType.BaseType==typeof(Task)&&Method.ReturnParameter.ParameterType.IsGenericType
                        ?Method.ReturnParameter.ParameterType.GetGenericArguments()[0]
                        :Method.ReturnParameter.ParameterType;
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            ShowType= Method.ReturnParameter.ParameterType,
                            SerializeType = type
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
                    pointItem.Title = "条件";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },

                            Title = "真",
                            ConnectorType = ConnectorType.Output
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },
                            Title = "假",
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
                            Title = "流输入"
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(bool)
                            },
                            Title = CustomScenarioGloble.GetI18N(typeof(bool).FullName)
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.OneToTwo:
                {
                    pointItem.Title = "一变二";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },

                            Title = "流输出",
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
                            Title = "流输出"
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
                            Title = "流输入"
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.OneToMany:
                {
                    pointItem.Title = "一变多";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(NodeConnectorClass)
                            },
                            Title = "流输出",
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
                            Title = "流输出"
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
                            Title = "流输入"
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(int),
                                Value = (double)2,
                                IsSelf = true
                            },


                            SelfInputAble = false,
                            Title = "输出数量"
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.Equal:
                {
                    pointItem.Title = "相等";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(bool)
                            },
                            Title = CustomScenarioGloble.GetI18N(typeof(bool).FullName),
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
                            Title = "流输入"
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(object)
                            },
                            Title = CustomScenarioGloble.GetI18N(typeof(object).FullName)
                        },
                        new ConnectorItem
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue
                            {
                                SerializeType = typeof(object)
                            },
                            Title = CustomScenarioGloble.GetI18N(typeof(object).FullName)
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

                        Title = "流输入"
                    });
                    inpItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = ValueDataType
                        },

                        Title = "设置"
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
                        Title = "流输出"
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
                        Title = "流输入"
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
                        Title = "流输出"
                    });
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = ValueDataType
                        },

                        Title = "获取",
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

                        Title = "流输入"
                    });
                    inpItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(object)
                        },

                        Title = "设置"
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
                        Title = "流输出"
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
                        Title = "流输入"
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
                        Title = "流输出"
                    });
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(object)
                        },

                        Title = "获取",
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
                        Title = "流输入"
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
                        Title = "流输出"
                    });
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = ValueDataType
                        },

                        Title = "获取",
                        ConnectorType = ConnectorType.Output
                    });
                    pointItem.Output = outItems;
                    break;
                }
                case ScenarioMethodType.OpenRunLocalProject:
                {
                    pointItem.Title = "打开/运行本地项目";
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem
                    {
                        Source = pointItem,
                        ConnectorType = ConnectorType.Output,
                        InputObject = new CustomScenarioValue
                        {
                            SerializeType = typeof(NodeConnectorClass)
                        },
                        Title = "流输出"
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
                            Title = "流输入"
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


                            Title = "本地项目"
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