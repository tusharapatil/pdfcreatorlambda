using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;
using Amazon.Lambda;
using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;
using Amazon.Lambda.S3Events;

using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;

using ReportPDFCreator;
using System.IO;

namespace ReportPDFCreator.Tests
{
    public class FunctionTest
    {
        [Fact]
        public async Task TestS3EventLambdaFunction()
        {
            string reportXML = File.ReadAllText(@"C:\Users\px2kpc\Desktop\pdflambda\xmlReport.xml");
     Parallel.For(0,1, i =>{ 

                IAmazonS3 s3Client = new AmazonS3Client();

                var bucketName = "emn-dev-content";
                var key = "Report/Xml/" + Guid.NewGuid().ToString();

                try
                {
                    var response = s3Client.PutObjectAsync(new PutObjectRequest
                    {
                        BucketName = bucketName,
                        Key = key,
                        ContentBody = reportXML
                    }).Result;

                    // Setup the S3 event object that S3 notifications would create with the fields used by the Lambda function.
                    var s3Event = new S3Event
                    {
                        Records = new List<S3EventNotification.S3EventNotificationRecord>
                      {
                        new S3EventNotification.S3EventNotificationRecord
                        {
                            S3 = new S3EventNotification.S3Entity
                            {
                                Bucket = new S3EventNotification.S3BucketEntity {Name = bucketName },
                                Object = new S3EventNotification.S3ObjectEntity {Key = key }
                            }
                        }
                      }
                    };

                    // Invoke the lambda function and confirm the content type was returned.
                    var function = new Function(s3Client);
                    var result =  function.FunctionHandler(s3Event, null);

                    //Assert.True(result);

                }
                finally
                {
                    // Clean up the test data
                    //await AmazonS3Util.DeleteS3BucketWithObjectsAsync(s3Client, bucketName);
                }
            });
              

        }
    }
}
