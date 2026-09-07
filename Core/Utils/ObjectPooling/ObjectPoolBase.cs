namespace T3.Core.Utils.ObjectPooling;

public interface ObjectPoolBase<T>
{
    public T Get();
    public void Return(T obj);
}