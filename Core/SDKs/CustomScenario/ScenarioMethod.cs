using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using Core.SDKs.Tools;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Attribute.Scenario;

namespace Core.SDKs.CustomScenario;

public class ScenarioMethod
{
    public ScenarioMethod()
    {
    }

    public ScenarioMethod(MethodInfo method, PluginInfo pluginInfo, ScenarioMethodAttribute attribute,
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
    public object TypeDate { get; set; }
    [JsonIgnore] public MethodInfo Method { get; set; }
    public PluginInfo? PluginInfo { get; set; }

    [JsonConverter(typeof(ScenarioMethodAttributeJsonCtr))]
    public ScenarioMethodAttribute Attribute { get; set; }

    public string _methodAbsolutelyName;

    public string MethodAbsolutelyName
    {
        get
        {
            if (Type == ScenarioMethodType.插件方法)
            {
                StringBuilder sb = new StringBuilder("|");

                foreach (var genericArgument in Method.GetParameters())
                {
                    var plugin = PluginManager.EnablePlugin
                        .FirstOrDefault((e) => e.Value._dll == genericArgument.ParameterType.Assembly)
                        .Value;
                    // type.Assembly.
                    // var a = PluginManager.GetPlugnNameByTypeName(type.FullName);
                    if (plugin is null)
                    {
                        sb.Append($"System {genericArgument.ParameterType.FullName}");
                        sb.Append("|");
                        continue;
                    }

                    sb.Append($"{PluginInfo.ToPlgString()} {genericArgument.ParameterType.FullName}");
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
        set { _methodAbsolutelyName = value; }
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
                Type = typeof(NodeConnectorClass),
                Title = "流输入",
                TypeName = "节点"
            });
            int autoUnboxIndex = 0;
            for (var index = 0;
                 index < Method.GetParameters()
                     .Length;
                 index++)
            {
                var parameterInfo = Method.GetParameters()[index];
                if (parameterInfo.ParameterType.FullName == "System.Threading.CancellationToken")
                {
                    continue;
                }

                bool IsSelf = parameterInfo.GetCustomAttributes(typeof(SelfInput))
                    .Any();

                if (parameterInfo.ParameterType.GetCustomAttribute(typeof(AutoUnbox)) is not null)
                {
                    autoUnboxIndex++;
                    var type = parameterInfo.ParameterType;
                    foreach (var memberInfo in type.GetProperties())
                    {
                        List<string>? interfaces = null;
                        if (!memberInfo.PropertyType.FullName.StartsWith("System."))
                        {
                            interfaces = new();
                            foreach (var @interface in memberInfo.PropertyType.GetInterfaces())
                            {
                                interfaces.Add(@interface.FullName);
                            }
                        }

                        inpItems.Add(new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = memberInfo.PropertyType,
                            IsSelf = IsSelf,
                            AutoUnboxIndex = autoUnboxIndex,
                            Interfaces = interfaces,
                            Title = Attribute.GetParameterName(memberInfo.Name),
                            TypeName = ScenarioMethodI18nTool.GetI18N(memberInfo.PropertyType.FullName),
                        });
                    }
                }
                else
                {
                    var connectorItem = new ConnectorItem()
                    {
                        Source = pointItem,
                        Type = parameterInfo.ParameterType,
                        IsSelf = IsSelf,
                        Title = Attribute.GetParameterName(parameterInfo.Name),
                        TypeName = ScenarioMethodI18nTool.GetI18N(parameterInfo.ParameterType.FullName)
                    };
                    if (parameterInfo.ParameterType.GetCustomAttribute<CustomNodeInputType>() is not null
                        and var customNodeInputType)
                    {
                        connectorItem.isPluginInputConnector = true;
                        connectorItem.IsSelf = true;
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

            if (Method.ReturnParameter.ParameterType != typeof(void))
            {
                ObservableCollection<ConnectorItem> outItems = new();
                outItems.Add(new ConnectorItem()
                {
                    Source = pointItem,
                    IsOut = true,
                    Type = typeof(NodeConnectorClass),
                    Title = "流输出",
                    TypeName = "节点"
                });
                if (Method.ReturnParameter.ParameterType.GetCustomAttribute(typeof(AutoUnbox)) is not null)
                {
                    autoUnboxIndex++;
                    var type = Method.ReturnParameter.ParameterType;
                    foreach (var memberInfo in type.GetProperties())
                    {
                        List<string>? interfaces = null;
                        if (!memberInfo.PropertyType.FullName.StartsWith("System."))
                        {
                            interfaces = new();
                            foreach (var @interface in memberInfo.PropertyType.GetInterfaces())
                            {
                                interfaces.Add(@interface.FullName);
                            }
                        }

                        outItems.Add(new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = memberInfo.PropertyType,
                            AutoUnboxIndex = autoUnboxIndex,
                            Interfaces = interfaces,
                            Title = Attribute.GetParameterName(memberInfo.Name),
                            TypeName = ScenarioMethodI18nTool.GetI18N(memberInfo.PropertyType.FullName),
                            IsOut = true
                        });
                    }
                }
                else
                {
                    List<string> interfaces = new();
                    foreach (var @interface in Method.ReturnParameter.ParameterType.GetInterfaces())
                    {
                        interfaces.Add(@interface.FullName);
                    }


                    outItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        Type = Method.ReturnParameter.ParameterType,
                        Title = Attribute.GetParameterName("return"),
                        Interfaces = interfaces,
                        TypeName =
                            ScenarioMethodI18nTool.GetI18N(Method.ReturnParameter.ParameterType.FullName),
                        IsOut = true
                    });
                }


                pointItem.Output = outItems;
            }


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
                            Type = typeof(NodeConnectorClass),
                            Title = "真",
                            TypeName = ScenarioMethodI18nTool.GetI18N(typeof(NodeConnectorClass).FullName),
                            IsOut = true
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(NodeConnectorClass),
                            Title = "假",
                            TypeName = ScenarioMethodI18nTool.GetI18N(typeof(NodeConnectorClass).FullName),
                            IsOut = true
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(NodeConnectorClass),
                            Title = "流输入",
                            TypeName = "节点"
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(bool),
                            Title = ScenarioMethodI18nTool.GetI18N(typeof(bool).FullName),
                            TypeName = ScenarioMethodI18nTool.GetI18N(typeof(bool).FullName)
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
                            Type = typeof(NodeConnectorClass),
                            Title = "流输出",
                            IsOut = true,
                            TypeName = "节点"
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(NodeConnectorClass),
                            IsOut = true,
                            Title = "流输出",
                            TypeName = "节点"
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(NodeConnectorClass),
                            Title = "流输入",
                            TypeName = "节点"
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
                            Type = typeof(NodeConnectorClass),
                            Title = "流输出",
                            IsOut = true,
                            TypeName = "节点"
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(NodeConnectorClass),
                            IsOut = true,
                            Title = "流输出",
                            TypeName = "节点"
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(NodeConnectorClass),
                            Title = "流输入",
                            TypeName = "节点"
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(int),
                            InputObject = (double)2,
                            IsSelf = true,
                            SelfInputAble = false,
                            Title = "输出数量",
                            TypeName = "整数"
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
                            Type = typeof(bool),
                            Title = ScenarioMethodI18nTool.GetI18N(typeof(bool).FullName),
                            TypeName = ScenarioMethodI18nTool.GetI18N(typeof(bool).FullName),
                            IsOut = true
                        }
                    };
                    pointItem.Output = StringoutItems;
                    ObservableCollection<ConnectorItem> StringinItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(NodeConnectorClass),
                            Title = "流输入",
                            TypeName = "节点"
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(object),
                            Title = ScenarioMethodI18nTool.GetI18N(typeof(object).FullName),
                            TypeName = ScenarioMethodI18nTool.GetI18N(typeof(object).FullName)
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(object),
                            Title = ScenarioMethodI18nTool.GetI18N(typeof(object).FullName),
                            TypeName = ScenarioMethodI18nTool.GetI18N(typeof(object).FullName)
                        }
                    };
                    pointItem.Input = StringinItems;
                    break;
                }
                case ScenarioMethodType.变量设置:
                {
                    pointItem.Title = $"变量{TypeDate}";
                    ObservableCollection<ConnectorItem> inpItems = new();
                    inpItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        Type = typeof(NodeConnectorClass),
                        Title = "流输入",
                        TypeName = "节点"
                    });
                    inpItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        Type = typeof(object),
                        Title = "设置",
                        TypeName = "变量"
                    });
                    pointItem.Input = inpItems;
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        Type = typeof(NodeConnectorClass),
                        Title = "流输出",
                        TypeName = "节点"
                    });
                    pointItem.Output = outItems;
                    break;
                }
                case ScenarioMethodType.变量获取:
                {
                    pointItem.Title = $"变量{TypeDate}";
                    ObservableCollection<ConnectorItem> inpItems = new();
                    inpItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        Type = typeof(NodeConnectorClass),
                        Title = "流输入",
                        TypeName = "节点"
                    });
                    pointItem.Input = inpItems;
                    ObservableCollection<ConnectorItem> outItems = new();
                    outItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        Type = typeof(NodeConnectorClass),
                        Title = "流输出",
                        TypeName = "节点"
                    });
                    outItems.Add(new ConnectorItem()
                    {
                        Source = pointItem,
                        Type = typeof(object),
                        Title = "获取",
                        TypeName = "变量"
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
                        Type = typeof(NodeConnectorClass),
                        Title = "流输出",
                        TypeName = "节点"
                    });
                    pointItem.Output = outItems;
                    ObservableCollection<ConnectorItem> pointInItems = new()
                    {
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(NodeConnectorClass),
                            Title = "流输入",
                            TypeName = "节点"
                        },
                        new ConnectorItem()
                        {
                            Source = pointItem,
                            Type = typeof(string),
                            RealType = typeof(SearchViewItem),
                            InputObject = "",
                            Title = "本地项目",
                            TypeName = "字符串",
                            IsSelf = true
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