using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Core.JsonConverter;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using Core.SDKs.Tools;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Attribute.Scenario;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace Core.SDKs.CustomScenario;

public class ScenarioMethod
{
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

    public string _methodAbsolutelyName;

    public string MethodAbsolutelyName
    {
        get
        {
            if (Type == ScenarioMethodType.插件方法)
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
        var pointItem = new ScenarioMethodNode()
        {
            ScenarioMethod = this,
            Title = MethodTitle
        };
        if (IsFromPlugin)
        {
            ObservableCollection<ConnectorItem> inpItems = new();
            inpItems.Add(new ConnectorItem()
            {
                Source = pointItem,
                InputObject = new CustomScenarioValue()
                {
                    Type = typeof(NodeConnectorClass)
                },

                Title = "流输入",
               
            });
            var autoUnboxIndex = 0;
            for (var index = 0;
                 index < Method.GetParameters()
                     .Length;
                 index++)
            {
                var parameterInfo = Method.GetParameters()[index];
                if (parameterInfo.ParameterType.FullName == "System.Threading.CancellationToken") continue;
                if (parameterInfo.ParameterType.FullName.StartsWith("System.Nullable`1[[System.Threading.CancellationToken,")) continue;
                var IsSelf = parameterInfo.GetCustomAttributes(typeof(SelfInput))
                    .Any();
                object? defaultValue = parameterInfo.DefaultValue;

                if (parameterInfo.ParameterType.GetCustomAttribute(typeof(AutoUnbox)) is not null)
                {
                    autoUnboxIndex++;
                    var type = parameterInfo.ParameterType;
                    foreach (var memberInfo in type.GetProperties())
                    {
                        if (memberInfo.GetCustomAttribute(typeof(AutoUnboxProperty)) is  null)
                        {
                            continue;
                        }
                        List<string>? interfaces = null;
                        if (!memberInfo.PropertyType.FullName.StartsWith("System."))
                        {
                            interfaces = new List<string>();
                            foreach (var @interface in memberInfo.PropertyType.GetInterfaces())
                                interfaces.Add(@interface.FullName);
                        }

                        inpItems.Add(new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = memberInfo.PropertyType,
                                IsSelf = IsSelf,
                                Value = defaultValue
                            },
                            
                            AutoUnboxIndex = autoUnboxIndex,
                            AutoUnboxPropertyName = memberInfo.Name,
                            Interfaces = interfaces,
                            Title = Attribute.GetParameterName(memberInfo.Name),
                        });
                    }
                }
                else
                {
                    var connectorItem = new ConnectorItem()
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue()
                        {
                            Type = parameterInfo.ParameterType,
                            IsSelf = IsSelf,
                            Value = defaultValue
                        },

                        
                        Title = Attribute.GetParameterName(parameterInfo.Name),
                    };
                    if (parameterInfo.GetCustomAttribute<CustomNodeInputType>() is not null
                        and var customNodeInputType)
                    {
                        connectorItem.isPluginInputConnector = true;
                        connectorItem.InputObject.IsSelf = parameterInfo.GetCustomAttribute<SelfInput>()is not null;
                        connectorItem.InputObject.RealType = connectorItem.InputObject.Type;
                        connectorItem.InputObject.Type = customNodeInputType.Type;
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
            outItems.Add(new ConnectorItem()
            {
                Source = pointItem,
                IsOut = true,
                InputObject = new CustomScenarioValue()
                {
                    Type = typeof(NodeConnectorClass)
                },

                Title = "流输出",
            });
            if (Method.ReturnParameter.ParameterType != typeof(void))
            {
                
                if (Method.ReturnParameter.ParameterType.GetCustomAttribute(typeof(AutoUnbox)) is not null)
                {
                    autoUnboxIndex++;
                    var type = Method.ReturnParameter.ParameterType;
                    foreach (var memberInfo in type.GetProperties())
                    {
                        if (memberInfo.GetCustomAttribute(typeof(AutoUnboxProperty)) is  null)
                        {
                            continue;
                        }
                        List<string>? interfaces = null;
                        if (!memberInfo.PropertyType.FullName.StartsWith("System."))
                        {
                            interfaces = new List<string>();
                            foreach (var @interface in memberInfo.PropertyType.GetInterfaces())
                                interfaces.Add(@interface.FullName);
                        }

                        outItems.Add(new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = memberInfo.PropertyType
                            },

                            AutoUnboxIndex = autoUnboxIndex,
                            AutoUnboxPropertyName = memberInfo.Name,
                            Interfaces = interfaces,
                            Title = Attribute.GetParameterName(memberInfo.Name),
                            IsOut = true
                        });
                    }
                }
                else
                {
                    List<string> interfaces = new();
                    foreach (var @interface in Method.ReturnParameter.ParameterType.GetInterfaces())
                        interfaces.Add(@interface.FullName);


                    outItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue()
                        {
                            Type = Method.ReturnParameter.ParameterType
                        },

                        Title = Attribute.GetParameterName("return"),
                        Interfaces = interfaces,
                        IsOut = true
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
                case ScenarioMethodType.插件方法:
                    break;
                case ScenarioMethodType.判断:
                {
                    pointItem.Title = "判断";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(NodeConnectorClass)
                            },

                            Title = "真", 
                            IsOut = true
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(NodeConnectorClass)
                            },
                            Title = "假",
                            IsOut = true
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(NodeConnectorClass)
                            },
                            Title = "流输入",
                            
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(bool)
                            },
                            Title = CustomScenarioGloble.GetI18N(typeof(bool).FullName),
                            
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.一对二:
                {
                    pointItem.Title = "一对二";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(NodeConnectorClass)
                            },

                            Title = "流输出",
                            IsOut = true,
                           
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(NodeConnectorClass)
                            },
                            IsOut = true,
                            Title = "流输出",
                            
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(NodeConnectorClass)
                            },
                            Title = "流输入",
                            
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.一对多:
                {
                    pointItem.Title = "一对多";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(NodeConnectorClass)
                            },
                            Title = "流输出",
                            IsOut = true,
                            
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(NodeConnectorClass)
                            },
                            IsOut = true,
                            Title = "流输出",
                           
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(NodeConnectorClass)
                            },
                            Title = "流输入",
                            
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(int),
                                Value = (double)2,
                                IsSelf = true,
                            },

                           
                            SelfInputAble = false,
                            Title = "输出数量",
                          
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.相等:
                {
                    pointItem.Title = "相等";
                    ObservableCollection<ConnectorItem> StringoutItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(bool)
                            },
                            Title = CustomScenarioGloble.GetI18N(typeof(bool).FullName),
                            IsOut = true
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(NodeConnectorClass)
                            },
                            Title = "流输入",
                            
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(object)
                            },
                            Title = CustomScenarioGloble.GetI18N(typeof(object).FullName),
                           
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(object)
                            },
                            Title = CustomScenarioGloble.GetI18N(typeof(object).FullName),
                           
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.变量设置:
                {
                    pointItem.Title = $"{ValueName}";
                    ObservableCollection<ConnectorItem> inpItems = new();
                    inpItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue()
                        {
                            Type = typeof(NodeConnectorClass)
                        },

                        Title = "流输入",
                        
                    });
                    inpItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue()
                        {
                            Type = ValueDataType
                        },

                        Title = "设置",
                        
                    });
                    pointItem.Input = inpItems;
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        IsOut = true,
                        InputObject = new CustomScenarioValue()
                        {
                            Type = typeof(NodeConnectorClass)
                        },
                        Title = "流输出",
                      
                    });
                    pointItem.Output = outItems;
                    break;
                }
                case ScenarioMethodType.变量获取:
                {
                    pointItem.Title = $"{ValueName}";
                    ObservableCollection<ConnectorItem> inpItems = new();
                    inpItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue()
                        {
                            Type = typeof(NodeConnectorClass)
                        },
                        Title = "流输入",
                     
                    });
                    pointItem.Input = inpItems;
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue()
                        {
                            Type = typeof(NodeConnectorClass)
                        },
                        IsOut = true,
                        Title = "流输出",
                        
                    });
                    outItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue()
                        {
                            Type = ValueDataType
                        },

                        Title = "获取",
                        IsOut = true,
                     
                    });
                    pointItem.Output = outItems;
                    break;
                }
                case ScenarioMethodType.打开运行本地项目:
                {
                    pointItem.Title = "打开/运行本地项目";
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        InputObject = new CustomScenarioValue()
                        {
                            Type = typeof(NodeConnectorClass)
                        },
                        Title = "流输出",
                       
                    });
                    pointItem.Output = outItems;
                    ObservableCollection<ConnectorItem> pointInItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(NodeConnectorClass)
                            },
                            Title = "流输入",
                         
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            InputObject = new CustomScenarioValue()
                            {
                                Type = typeof(string),
                                RealType = typeof(SearchViewItem),
                                Value = "",
                                IsSelf = true
                            },


                            Title = "本地项目",
                        
                            
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