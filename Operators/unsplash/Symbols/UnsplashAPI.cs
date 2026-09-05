using Unsplasharp;
using Unsplasharp.Models;

namespace t3.unsplash;

[Guid("89162b9f-75f5-4d32-9d28-8259cf47cf58")]
[ExportDependencies("Unsplasharp.dll")]
public class UnsplashAPI : Instance<UnsplashAPI>
{
    [Output(Guid = "487C1FE8-5B7C-43FB-855D-CC2B3CA70566")]
    public readonly Slot<string> PhotoUrl = new();
        
    [Output(Guid = "99282081-C93A-4E12-B4A1-08062A762293")]
    public readonly Slot<string> PhotoAuthor = new();
        
    [Output(Guid = "B4C9E528-A173-4CE4-8EAF-77A05E004D5D")]
    public readonly Slot<List<string>> ResultList = new();

    public UnsplashAPI()
    {
        PhotoUrl.UpdateAction += Update;
        PhotoAuthor.UpdateAction += Update;
        ResultList.UpdateAction += Update;
    }

        
    private void Update(EvaluationContext context)
    {
        var search = Search.GetValue(context);
        var collectionId = CollectionId.GetValue(context);
        var triggerRequest = TriggerRequest.GetValue(context);
            
        if (triggerRequest && (search != _searchQuery ||  collectionId != _collectionId))
        {
            TriggerRequest.Value = false;
            TriggerRequest.TypedInputValue.Value = false;
            TriggerRequest.DirtyFlag.Invalidate();
            _maxResultCount = MaxResultCount.GetValue(context);
            _apiToken = ApiToken.GetValue(context);
            _searchQuery = search;
            _collectionId = collectionId;
            _request = SearchImagesTask();
        }

        var photoIndex = GetPhotoIndex.GetValue(context);
        if (photoIndex != _photoIndex)
        {
            _photoIndex = photoIndex;
            if (_photos != null && _photos.Count != 0)
            {
                var index = Math.Abs(_photoIndex) % _photos.Count;
                PhotoAuthor.Value = _photos[index].User.Name;
                PhotoUrl.Value = _photos[index].Urls.Regular;
                Log.Debug($"Update photo properties: Author {PhotoAuthor.Value}   Url {PhotoUrl.Value}", this);
            }
        }
    }

    private int _photoIndex = 0;
    private string _collectionId = string.Empty;

    private async Task SearchImagesTask()
    {
        var client = new UnsplasharpClient(_apiToken);


        List<Photo> photosFound = null;
        if (!string.IsNullOrEmpty(_collectionId))
        {
            photosFound = await client.GetCollectionPhotos(_collectionId, 1, _maxResultCount);
        }
        else
        {
            photosFound = await client.SearchPhotos(_searchQuery, 1, _maxResultCount);
        }
            
        _photos = photosFound;
        Log.Debug($"got {photosFound.Count} images from Unsplash", this);
        _urls.Clear();
        foreach (var p in photosFound)
        {
            _urls.Add(p.Urls.Regular);
        }
        ResultList.Value = _urls;
        _photoIndex = -1;
        ResultList.DirtyFlag.Invalidate();
    }

    private List<Photo> _photos = new();
    private string _searchQuery = String.Empty;
    private List<string> _urls = new();
    private int _maxResultCount = 100;
    private Task _request;
    private string _apiToken;
        
    [Input(Guid = "6D3F829B-C64E-45F2-9E8D-3844F4864C3A")]
    public readonly InputSlot<int> GetPhotoIndex = new();

    [Input(Guid = "8F164AA3-962A-4B08-970E-CB730CA10F9B")]
    public readonly InputSlot<string> ApiToken = new();

    [Input(Guid = "8F879A22-186D-45C3-B30C-AF909CC8E609")]
    public readonly InputSlot<bool> TriggerRequest = new();
        
    [Input(Guid = "6EE4BE4B-E0E9-40AA-AFEC-088B425791F1")]
    public readonly InputSlot<string> Search = new();

    [Input(Guid = "A506693E-2859-4D9D-AB85-0A0F51CC2DA9")]
    public readonly InputSlot<string> CollectionId = new();
        
    [Input(Guid = "B3B90281-2D2A-4D79-8A0D-5D902CFAAA35")]
    public readonly InputSlot<int> MaxResultCount = new(100);

}