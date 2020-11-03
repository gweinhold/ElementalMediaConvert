using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.MediaConvert;
using Amazon.MediaConvert.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace ConvertVideo
{

    public class Function
    {
        private readonly string _mediaConvertRole;
        private readonly AmazonMediaConvertClient _client;

        public Function()
        {
            _mediaConvertRole = Environment.GetEnvironmentVariable("MC_ROLE");
            var region = Environment.GetEnvironmentVariable("AWS_REGION");
            var regionEndpoint = RegionEndpoint.GetBySystemName(region);
            var mediaConvertEndpoint = ListEndpoint(regionEndpoint).Result;

            var config = new AmazonMediaConvertConfig { ServiceURL = mediaConvertEndpoint };
            _client = new AmazonMediaConvertClient(config);
        }

        private async Task<string> ListEndpoint(RegionEndpoint region)
        {
            var config = new AmazonMediaConvertConfig { RegionEndpoint = region };
            var client = new AmazonMediaConvertClient(config);
            var request = new DescribeEndpointsRequest();
            var response = await client.DescribeEndpointsAsync(request);

            return response.Endpoints.FirstOrDefault()?.Url;
        }

        public async Task FunctionHandler(S3Event s3Event, ILambdaContext context)
        {
            foreach (var record in s3Event.Records)
            {
                var s3Bucket = record.S3.Bucket.Name;
                var inputFile = record.S3.Object.Key;
                var s3InputUri = $"s3://{s3Bucket}/{inputFile}";
                var createJobStatus = await CreateJob(_client, _mediaConvertRole, s3InputUri, s3Bucket);
                Console.WriteLine($"Response from MediaConvert: {createJobStatus} for {inputFile}");
            }
        }

        private async Task<JobStatus> CreateJob(AmazonMediaConvertClient client, string mediaConvertRole, string inputFile, string s3Bucket)
        {
            var request = new CreateJobRequest();

            var input = new Input()
            {
                FileInput = inputFile,
                AudioSelectors = new Dictionary<string, AudioSelector>
                {
                    {
                        "Audio Selector 1", new AudioSelector
                            {
                                Offset = 0,
                                DefaultSelection = AudioDefaultSelection.DEFAULT,
                                ProgramSelection = 1,
                            }
                    }
                },
                VideoSelector = new VideoSelector { ColorSpace = ColorSpace.FOLLOW },
                FilterEnable = InputFilterEnable.AUTO,
                PsiControl = InputPsiControl.USE_PSI,
                DeblockFilter = InputDeblockFilter.DISABLED,
                DenoiseFilter = InputDenoiseFilter.DISABLED,
                TimecodeSource = InputTimecodeSource.EMBEDDED,
                FilterStrength = 0
            };

            var outputGroup = new OutputGroup
            {
                Name = "File Group",
                OutputGroupSettings = new OutputGroupSettings
                {
                    Type = OutputGroupType.FILE_GROUP_SETTINGS,
                    FileGroupSettings = new FileGroupSettings { Destination = $"s3://{s3Bucket}/output/" }
                },
                Outputs = new List<Output>
                {
                    new Output
                    {
                        Preset = "System-Generic_Hd_Mp4_Avc_Aac_16x9_Sdr_1280x720p_30Hz_5Mbps_Qvbr_Vq9",
                        Extension = "mp4",
                        NameModifier = "_Generic720",
                        ContainerSettings = new ContainerSettings
                        {
                            Container = "MP4",
                            Mp4Settings = new Mp4Settings
                            {
                                CslgAtom = Mp4CslgAtom.INCLUDE,
                                FreeSpaceBox = Mp4FreeSpaceBox.EXCLUDE,
                                MoovPlacement = Mp4MoovPlacement.PROGRESSIVE_DOWNLOAD
                            }
                        },
                    },
                    new Output
                    {
                        Preset = "System-Generic_Hd_Mp4_Avc_Aac_16x9_1920x1080p_60Hz_9Mbps",
                        Extension = "mp4",
                        NameModifier = "_Generic1080"
                    }
                }
            };

            var jobSettings = new JobSettings
            {
                AdAvailOffset = 0,
                Inputs = new List<Input> { input },
                OutputGroups = new List<OutputGroup> { outputGroup }
            };
            request.Settings = jobSettings;
            request.Role = mediaConvertRole;

            var response = await client.CreateJobAsync(request);

            return response.Job.Status;
        }

    }
}
