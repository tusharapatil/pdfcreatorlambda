using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Xsl;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DinkToPdf;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace ReportPDFCreator
{
    public class Function
    {
        IAmazonS3 S3Client { get; set; }
        static PdfTools pdfTools = new PdfTools();
        static SynchronizedConverter syncConverter = new SynchronizedConverter(pdfTools);
        
        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public Function()
        {
            S3Client = new AmazonS3Client();
        }

        /// <summary>
        /// Constructs an instance with a preconfigured S3 client. This can be used for testing the outside of the Lambda environment.
        /// </summary>
        /// <param name="s3Client"></param>
        public Function(IAmazonS3 s3Client)
        {
            this.S3Client = s3Client;
        }

        /// <summary>
        /// This method is called for every Lambda invocation. This method takes in an S3 event object and can be used 
        /// to respond to S3 notifications.
        /// </summary>
        /// <param name="evnt"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public bool FunctionHandler(S3Event evnt, ILambdaContext context)
        {
            
            bool result = false;
            var s3Event = evnt.Records?[0].S3;
            if (s3Event == null)
            {
                return false;
            }

            try
            {
                if (context != null)
                    context.Logger.LogLine("Lambda got called");
                RegionEndpoint region = RegionEndpoint.EUWest1;
                string bucketName = s3Event.Bucket.Name;
                string keyString = s3Event.Object.Key;
                string[] keyParts = keyString.Split("/");
                string analysisId = keyParts[keyParts.Length - 1];

                var response = S3Client.GetObjectAsync(bucketName, keyString).Result;

                if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    return false;
                }
                TextReader textReader = new StreamReader(response.ResponseStream);
                var xmlContent = textReader.ReadToEndAsync().Result;

                if (context != null)
                    context.Logger.LogLine($"Value of analysisId is {analysisId}");
                context.Logger.LogLine($"using SynchronizedConverter");
                context.Logger.LogLine($"using normal execution no async");
                CreatePdf(analysisId, bucketName, xmlContent, context);
                var analysisIdWithoutExtension = analysisId.Replace(".xml", "");
                IAmazonDynamoDB amazonDynamo = new AmazonDynamoDBClient(region);
                Dictionary<string, AttributeValue> items = new Dictionary<string, AttributeValue>();
                var request = new UpdateItemRequest
                {
                    TableName = "DFM",
                    Key = new Dictionary<string, AttributeValue>() { { "analysisId", new AttributeValue { S = analysisIdWithoutExtension } } },
                    ExpressionAttributeNames = new Dictionary<string, string>()
                    {
                        {"#S", "jobStatus"}
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>()
                    {
                        {":S",new AttributeValue { S = "PDFCreated" }},
                    },
                    UpdateExpression = "SET #S = :S"
                };
                var updateRequestResult = amazonDynamo.UpdateItemAsync(request).Result;
                if (updateRequestResult.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    result = true;
                }
                else
                {
                    result = false;
                }
                if (context != null)
                    context.Logger.LogLine($"Value added in DDB with analysisId :{analysisIdWithoutExtension}");
            }
            catch (Exception e)
            {
                context.Logger.LogLine($"Error getting object {s3Event.Object.Key} from bucket {s3Event.Bucket.Name}. Make sure they exist and your bucket is in the same region as this function.");
                context.Logger.LogLine(e.Message);
                context.Logger.LogLine(e.StackTrace);
                throw;
            }
            finally
            {
                context.Logger.LogLine($"Remaining Time {context.RemainingTime.Seconds}");

            }
          
            return result;
        }
        private bool CreatePdf(string analysisId, string bucketName, string xmlString, ILambdaContext context)
        {
            bool result = false;
            try
            {

                context.Logger.LogLine($"started pdf creation");
                context.Logger.LogLine($"Memory utilization {context.MemoryLimitInMB} timeremaining :{context.RemainingTime.Seconds}");
                string path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string xsltPath = Path.Combine(path, "reportXslt.xslt");
                string xsltString = "";
                using (TextReader textReader = new StreamReader(xsltPath))

                {
                    xsltString = textReader.ReadToEndAsync().Result;
                }
                // logger.LogInformation($"XmlToPdfService: CreatePdf: xslt content : {xsltString.Trim()}");
                XslCompiledTransform transform = new XslCompiledTransform();
                using (XmlReader reader = XmlReader.Create(new StringReader(xsltString)))
                {
                    transform.Load(reader);
                }
                string html = "";
                using (StringWriter results = new StringWriter())
                {

                    using (XmlReader reader = XmlReader.Create(new StringReader(xmlString)))
                    {
                        transform.Transform(reader, null, results);
                    }
                    html = results.ToString();
                }
                // logger.LogInformation($"XmlToPdfService: CreatePdf: html content : {html.Trim()}");


                var doc = new HtmlToPdfDocument()
                {
                    GlobalSettings = {
                    ColorMode = ColorMode.Color,
                    Orientation = Orientation.Portrait,
                    PaperSize = PaperKind.A4
                },
                    Objects = {
                    new ObjectSettings() {
                        PagesCount = true,
                        HtmlContent = html,
                        WebSettings = { DefaultEncoding = "utf-8" }
                    }
                }
                };
                byte[] pdfBytes = null;
             
                    context.Logger.LogLine($"Memory limit {context.MemoryLimitInMB} timeremaining :{context.RemainingTime.Seconds}");
                    pdfBytes = syncConverter.Convert(doc);
                    context.Logger.LogLine($"Memory limit {context.MemoryLimitInMB} timeremaining :{context.RemainingTime.Seconds}");
                    var analysisIdWithoutExtension = analysisId.Replace(".xml", "");
                    using (MemoryStream memoryStreamForPdf = new MemoryStream(pdfBytes))
                    {

                        var response = S3Client.PutObjectAsync(new PutObjectRequest()
                        {
                            BucketName = bucketName,
                            Key = @"Report/Pdf/" + analysisIdWithoutExtension,
                            ContentType = "application/pdf",
                            InputStream = memoryStreamForPdf
                        }).Result;
                        if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
                        {
                            result = false;
                        }

                    }



                result = true;
            }
            catch (Exception e)
            {
                context.Logger.LogLine($"Error in this function.");
                context.Logger.LogLine(e.Message);
                context.Logger.LogLine(e.StackTrace);
                throw;
            }
            return result;

        }
    }
}
