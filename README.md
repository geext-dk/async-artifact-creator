# AsyncArtifactCreator
![NuGet badge](https://img.shields.io/nuget/v/AsyncArtifactCreator)

A simple package containing a hosted service that helps its users create an
asynchronous artifact creation service. The artifacts can be whatever one
wants, for instance:
- Processed images
- Big archives
- Results of complex database queries

## Sample usage
To use the class, one must create a derived class and implement the necessary
methods, one class for storing data for artifact creation
and one more class for storing the final artifact.

```c#
public class ArtifactCreationData
{
    public int Id { get; set; }
    public byte[] ImageBytes { get; set; }
}

public class FinalArtifact
{
    public int Id { get; set; }
    public byte[] ImageBytes { get; set; }
}

// The first type parameter is the data that is needed for creating an
// artifact
public class SampleArtifactCreator
    : AsyncArtifactCreator<ArtifactCreationData, FinalArtifact, int>
{
    // The constructor is optional
    public SampleArtifactCreator(ILogger<SampleArtifactCreator> logger = null)
        : base(logger) { }

    // Required. Defines the task for
    protected override async Task<FinalArtifact> ProduceArtifact(
        ArtifactCreationData request,
        CancellationToken cancellationToken = default)
    {
        var newBytes = await SomeLongRunningImageProcessingTask(request.Id,
            request.ImageBytes);
    
        return new FinalArtifact
        {
            Id = request.Id,
            ImageBytes = newBytes
        };
    }

    // Required. Defines, how to obtain an id from the request
    protected override int GetIdentity(ArtifactCreationData request)
    {
        return request.Id;
    }

    // Required. Defines, how to obtain an id from the artifact
    protected override int GetIdentity(FinalArtifact artifact)
    {
        return artifact.Id;
    }

    private async Task<byte[]> SomeLongRunningImageProcessingTask(int id,
        byte[] imageBytes)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
    
        // It's possible to update the progress on the task. Defaults to 0.0.
        // Must be between 0 and 1, or else it will be clamped.
        ReportProgress(id, 0.5);
        await Task.Delay(TimeSpan.FromSeconds(3));
        ReportProgress(id, 1.0);
    
        return imageBytes;
    }
}
```