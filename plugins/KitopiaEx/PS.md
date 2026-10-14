''''

```c#
[SearchMethod]
    public SearchViewItem a1(string search)
    {
        var action = new Action<SearchViewItem>((e) =>
        {
            MessageBox.Show(e.OnlyKey);
        });
        return new SearchViewItem()
        {
            FileName = "内容来自插件" + search,
            FileType = FileType.自定义,
            OnlyKey = search,
            Icon = null,
            Action = action,
            IconSymbol = 0xF6EC,
            IsVisible = true
        };
    }
```

```c#
[AutoUnbox]
public class Result
{
    public Result(string name, int id)
    {
        Name = name;
        Id = id;
    }

    public string Name
    {
        get;
        set;
    }

    public int Id
    {
        get;
        set;
    }
}
```

```c#
    [PluginMethod("测试代码3", $"{nameof(id)}=参数1", $"{nameof(id2)}=参数2", $"{nameof(id3)}=参数3", "Id=ID", "Name=名字",
        "return=返回参数")]
    public Result t(string id, string id2, int id3)
    {
        return new Result(name: $"{id}_{id2}", id: id3);
    }

    [PluginMethod("测试代码4", $"{nameof(id)}=参数1", "return=返回参数")]
    public void t(string id2, Result id, int id3)
    {
        MessageBox.Show(id.Name + " " + id.Id + " " + id3);
    }
```

```c#
public class Test1
{
    [PluginMethod("测试代码1", $"{nameof(id)}=参数1", $"{nameof(id2)}=参数2", "Id=ID", "Name=名字", "return=返回参数")]
    public B t(string id, string id2)
    {
        return new B() { Name = $"{id}_{id2}" };
    }

    [PluginMethod("测试代码2", $"{nameof(id)}=参数1", "return=返回参数")]
    public void t2(Abase id)
    {
        MessageBox.Show(id.Name);
    }

    [ConfigField("名称", "设置名称")] public string name = "1";
}

public interface Abase
{
    public string Name
    {
        get;
        set;
    }
}

public class B : Abase
{
    public string Name
    {
        get;
        set;
    }
}
```