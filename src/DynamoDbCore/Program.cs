using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Runtime;
using Amazon.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DynamoDBCore
{
    /*public*/

    class Program
    {
        private readonly IServiceProvider serviceProvider;
        static IConfigurationRoot Configuration { get; set; }

        // this is the vanilla way using project.json and user secrets, instead of using apsettings.json
        private static /*readonly*/ AmazonDynamoDBClient dynamoclient;

        //public Program(IApplicationEnvironment env)
        //{
        //}

        //public Program(IApplicationEnvironment env, IServiceManifest serviceManifest)
        //{
        //    var services = new ServiceCollection();
        //    ConfigureServices(services);
        //    serviceProvider = services.BuildServiceProvider();
        //}

        //private void ConfigureServices(IServiceCollection services)
        //{
        //}

        public static void Main(string[] args)
        {

            // run: java -Djava.library.path=./DynamoDBLocal_lib -jar DynamoDBLocal.jar -sharedDb -port 5555
            // in the folder where dynamodb jar is listed

            // this is the profile manager way
            //ProfileManager.RegisterProfile("AWS-mgrace", "AKIAJRJVSHP3ATMMDGRQ", "3bUmOvz5n13iSrVQ9LR/wN7suhzYan2ebPF13O9R");

            // this is the way using project.json and user secrets

            #region project.json way

            var config = new ConfigurationBuilder()
                .AddUserSecrets("RemoteDynamoDB")
                .Build();
            var accessKey = config["aws-access-key"];
            var secretKey = config["aws-secret-key"];
            dynamoclient = BuildClient(accessKey, secretKey);
            //Demo();
            //BuildGracePermissionsSpecialTable(); //comment out - this is the special permissions table
            //LoadGraceSpecialPermsTable(); // comment out - this is importing data into special permissions table
            Console.WriteLine("Building Customer table [SummitCustomer].");
            BuildSummitCustomer();
            Console.WriteLine("Importing data.");
            LoadCustomerAndImport(); // this doesn't error, but also doesnt update the structure, as i expected
            var customers = ReadCustomerTable();
            Console.WriteLine("Scanned customers table.");
            Console.WriteLine();
            foreach (var cust in customers)
            {
                Console.WriteLine($"Customer: {cust}");
            }
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("*** Retrieving customer by Id (Id = 3) ***");
            var customer1 = GetCustomerById(id: 3, lastname: "Validator");
            Console.WriteLine($"Customer: {customer1}");

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("*** Retrieving customer by LastName (lastname = McTester) ***");
            var customer2 = GetCustomerByLastName(lastname: "McTester");
            Console.WriteLine($"Customer: {customer2}");

            #endregion project.json way


            // this is the way using appsettings.json

            #region appsettings way
            /*
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddInMemoryCollection(new[] // this is not really related to dynamo, just me trying out stuff
                {
                    new KeyValuePair<string, string>("the-key", "the-value"),
                    new KeyValuePair<string, string>("my-other-key", "other-key-value"),
                });
            Configuration = builder.Build();


            var configValue = Configuration["my-other-key"];
            Console.WriteLine($"The value for 'my-other-key' is '{configValue}'");

            var options = Configuration.GetAWSOptions();
            try
            {
                AmazonDynamoDBClient ddclient = options.CreateServiceClient<AmazonDynamoDBClient>();

                var customergracetable = Table.LoadTable(ddclient, "gracecustomer");
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception occured during configuration of amazon dynamoDb client");
            }
            // use it once we figure out how to write this up above
            //services.AddAWSService<IAmazonS3>();
            //services.AddAWSService<IAmazonDynamoDB>();

            // DO ALL DYNAMO TESTING HERE
            CreateDynamoTable();
            ImportTable();

            */
            #endregion appsettings way

        } // end of main

        #region project.json and user secrets way

        private static AmazonDynamoDBClient BuildClient(string accessKey, string secretKey)
        {
            Console.WriteLine("Creating DynamoDB client...");
            var credentials = new BasicAWSCredentials(
                accessKey: accessKey,
                secretKey: secretKey);
            var config = new AmazonDynamoDBConfig
            {
                RegionEndpoint = RegionEndpoint.USWest2
            };
            return new AmazonDynamoDBClient(credentials, config);
        }

        //private static async Task<TableDescription> BuildOrDescribeTable()
        private static TableDescription BuildOrDescribeTable()
        {
            // Define the cancellation token.
            CancellationTokenSource source = new CancellationTokenSource();
            CancellationToken token = source.Token;

            // build the widget table
            var request = new CreateTableRequest(
                tableName: "Widgets",
                keySchema: new List<KeySchemaElement>
                {
                    new KeySchemaElement
                    {
                        AttributeName = "WidgetId",
                        KeyType = KeyType.HASH
                    }
                },
                attributeDefinitions: new List<AttributeDefinition>
                {
                    new AttributeDefinition()
                    {
                        AttributeName = "WidgetId",
                        AttributeType = ScalarAttributeType.S
                    }
                },
                provisionedThroughput: new ProvisionedThroughput
                {
                    ReadCapacityUnits = 1,
                    WriteCapacityUnits = 1
                }
            );
            Console.WriteLine("Sending request to build Widgets table...");
            try
            {
                //var result = await dynamoclient.CreateTableAsync(request);
                var result = dynamoclient.CreateTableAsync(request).Result;
                Console.WriteLine("Table created.");
                return result.TableDescription;
            }
            //catch (ResourceInUseException)
            catch (Exception e)
            {
                if (e.Message.Contains("Table already exists"))
                    // Table already created, just describe it
                    Console.WriteLine("Table already exists. Fetching description...");
                return /*await*/
                    dynamoclient.DescribeTableAsync(new DescribeTableRequest("Widgets"), token).Result.Table;
            }
        }

        private static void BuildSummitCustomer()
        {
            // Define the cancellation token.
            CancellationTokenSource source = new CancellationTokenSource();
            CancellationToken token = source.Token;

            // build the widget table
            var request = new CreateTableRequest
            {
                TableName = "SummitCustomer",
                KeySchema = new List<KeySchemaElement> // PK here
                {
                    new KeySchemaElement
                    {
                        AttributeName = "Id",
                        KeyType = KeyType.HASH
                    }
                },
                AttributeDefinitions = new List<AttributeDefinition> // fields here
                {
                    new AttributeDefinition()
                    {
                        AttributeName = "Id",
                        AttributeType = ScalarAttributeType.N
                    },
                    //-- DynamoDB does not have a fixed schema. Instead, each data item may have a different number of attributes (aside from the mandatory key attributes).
                    // -- below are non key fields.  Only hash and range fields need to be in the create table command
                    //new AttributeDefinition()
                    //{
                    //    AttributeName = "DTID",
                    //    AttributeType = ScalarAttributeType.N
                    //},
                    //new AttributeDefinition()
                    //{
                    //    AttributeName = "FirstName",
                    //    AttributeType = ScalarAttributeType.S
                    //},
                    //new AttributeDefinition()
                    //{
                    //    AttributeName = "LasttName",
                    //    AttributeType = ScalarAttributeType.S
                    //},
                    //---- add collection items here, list or sets and maps (document types)
                    //new Document() // eck, what is the syntax for this?
                    //{
                        
                    //}
                    //new AttributeDefinition()
                    //{
                    //    AttributeName = "Address",
                    //    AttributeType = 
                    //},

                },
                ProvisionedThroughput = new ProvisionedThroughput
                {
                    ReadCapacityUnits = 1,
                    WriteCapacityUnits = 1
                }
            };
            try
            {
                var result = dynamoclient.CreateTableAsync(request).Result;
                Console.WriteLine($"Table created: {result.TableDescription.TableName}");
            }
            catch (Exception e)
            {
                if (e.Message.Contains("Table already exists"))
                    // Table already created, just describe it
                    Console.WriteLine("Table [SummitCustomer] already exists.");
            }
        }

        private static void BuildGracePermissionsSpecialTable()
        {
            // Define the cancellation token.
            CancellationTokenSource source = new CancellationTokenSource();
            CancellationToken token = source.Token;

            // build the widget table
            var request = new CreateTableRequest
            {
                TableName = "GracePermissionsSpecial",
                KeySchema = new List<KeySchemaElement> // PK here
                {
                    new KeySchemaElement
                    {
                        AttributeName = "UserId",
                        KeyType = KeyType.HASH
                    }
                },
                AttributeDefinitions = new List<AttributeDefinition> // fields here
                {
                    new AttributeDefinition()
                    {
                        AttributeName = "UserId",
                        AttributeType = ScalarAttributeType.S
                    },
                },
                ProvisionedThroughput = new ProvisionedThroughput
                {
                    ReadCapacityUnits = 1,
                    WriteCapacityUnits = 1
                }
            };
            try
            {
                var result = dynamoclient.CreateTableAsync(request).Result;
                Console.WriteLine($"Table created: {result.TableDescription.TableName}");
            }
            catch (Exception e)
            {
                if (e.Message.Contains("Table already exists"))
                    // Table already created, just describe it
                    Console.WriteLine("Table [GracePermissionsSpecial] already exists.");
            }
        }

        //private static async Task SaveItem()
        private static void /*async Task*/ SaveItem()
        {
            try
            {
                Console.WriteLine("About to save item '123' to the Widgets table...");
                //await dynamoclient.PutItemAsync(
                dynamoclient.PutItemAsync(
                    tableName: "Widgets",
                    item: new Dictionary<string, AttributeValue>
                    {
                        {"WidgetId", new AttributeValue {S = "123"}},
                        {"Description", new AttributeValue {S = "This is a widget."}}
                    }
                );
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in saving item: {e.Message} {e.InnerException}");
            }
        }

        //private static async Task<Dictionary<string, AttributeValue>> FetchItem()
        private static Dictionary<string, AttributeValue> FetchItem()
        {
            try
            {
                Console.WriteLine("About to fetch item '123' from the Widgets table...");
                //var response = await dynamoclient.GetItemAsync(
                var response = dynamoclient.GetItemAsync(
                    tableName: "Widgets",
                    key: new Dictionary<string, AttributeValue>
                    {
                        {"WidgetId", new AttributeValue {S = "123"}}
                    }
                ).Result;
                return response.Item;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in saving item: {e.Message} {e.InnerException}");
            }
            return null;
        }

        //public static async Task Demo()
        public static void Demo()
        {
            // Define the cancellation token.
            CancellationTokenSource source = new CancellationTokenSource();
            CancellationToken token = source.Token;

            // perform the actual demo
            try
            {
                //var description = await BuildOrDescribeTable();
                var description = BuildOrDescribeTable();
                while (description == null || !TableStatus.ACTIVE.Equals(description.TableStatus))
                {
                    Console.WriteLine($"Table not ready yet. Status: {description?.TableStatus}. Sleeping 500 ms.");
                    Thread.Sleep(500);
                    description = /*await DescribeTable*/
                        dynamoclient.DescribeTableAsync(new DescribeTableRequest("Widgets"), token).Result.Table;
                }
                Console.WriteLine($"Table status: {description.TableStatus}");
                //await SaveItem();
                //var loadedItem = await FetchItem();
                SaveItem();
                var loadedItem = FetchItem();
                Console.WriteLine($"Item loaded. Description: {loadedItem["Description"].S}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in demo: {e.Message} {e.InnerException}");
            }
        }

        private static void LoadCustomerAndImport()
        {
            var table = Table.LoadTable(dynamoclient, "SummitCustomer"); //"gracecustomer");

            JArray customerArray = null;
            //using (StreamReader sr = new StreamReader("customerdata.json"))  // doesnt want to compile
            using (TextReader tr = File.OpenText("customerdata.json")) //what the crap?? why wont it let me use stream reader the 4.5 way?
            {
                using (JsonTextReader jtr = new JsonTextReader(tr))
                {
                    customerArray = (JArray)JToken.ReadFrom(jtr);
                }
            }
            foreach (var item in customerArray)
            {
                string itemJson = item.ToString();
                Document doc = Document.FromJson(itemJson);
                table.PutItemAsync(doc);
            }
        }

        private static void LoadGraceSpecialPermsTable()
        {
            var table = Table.LoadTable(dynamoclient, "GracePermissionsSpecial");

            JArray customerArray = null;
            using (TextReader tr = File.OpenText("gracespecialdata.json"))
            {
                using (JsonTextReader jtr = new JsonTextReader(tr))
                {
                    customerArray = (JArray)JToken.ReadFrom(jtr);
                }
            }
            foreach (var item in customerArray)
            {
                string itemJson = item.ToString();
                Document doc = Document.FromJson(itemJson);
                table.PutItemAsync(doc);
            }
        }

        private static List<string> ReadCustomerTable()
        {
            var result = new List<string>();
            var tableName = "SummitCustomer"; //"gracecustomer";
            var customerTable = Table.LoadTable(dynamoclient, tableName);

            var request = new ScanRequest
            {
                TableName = tableName
            };

            var scanResult = dynamoclient.ScanAsync(request).Result;

            foreach (Dictionary<string, AttributeValue> item in scanResult.Items)
            {
                // Process the result.
                var dtid = Int32.Parse(item["DTID"].N);
                var lastName = item["LastName"].S;
                var firstName = item["FirstName"].S;
                var id = Int32.Parse(item["Id"].N);
                var address = item["Address"].M;
                var addr1 = address["Addr1"].S;
                var city = address["City"].S;
                var state = address["State"].S;
                var zip = address["Zip"].S;
                var contacts = item["Contacts"].M;  // this doesn't work - how do you get contacts out? it's not recognized as a valid key
                var emails = contacts["Email_addresses"].SS ?? new List<string>();
                var mobile = contacts.ContainsKey("Mobile") ? (contacts["Mobile"].S ?? string.Empty) : string.Empty;
                var homephone = contacts.ContainsKey("Homephone") ? (contacts["Homephone"].S ?? string.Empty) : string.Empty;
                var workphone = contacts.ContainsKey("Workphone") ? (contacts["Workphone"].S ?? string.Empty) : string.Empty;
                var employer = contacts.ContainsKey("Employer") ? (contacts["Employer"].S ?? string.Empty) : string.Empty;

                var customerObj = new Customer
                {
                    LastName = lastName,
                    FirstName = firstName,
                    Id = id,
                    DTID = dtid,
                    Address1 = addr1,
                    City = city,
                    State = state,
                    Zip = zip,
                    EmailAddresses = emails,
                    MobilePhone = mobile,
                    WorkPhone = workphone,
                    HomePhone = homephone,
                    Employer = employer
                };
                result.Add(JsonConvert.SerializeObject(customerObj));
                // var deserialized = JsonConvert.DeserializeObject<SomeObject>(jsonString);
            }

            return result;
        }

        private static string GetCustomerById(int id, string lastname)
        {
            var request = new GetItemRequest
            {
                TableName = "SummitCustomer", //"gracecustomer",
                ProjectionExpression = "Id, DTID, LastName, FirstName",
                //ExpressionAttributeNames = new Dictionary<string, string> // expression attribute names are what we use for assigning param column names - e.g. @param in db2, but #var in aws.  We use [ExpressionAttributeValues] for the corresponding param values and do #var = :paramvalue
                //{
                //    { "#ln", "lastname" },
                //    { "#fn", "firstname" }
                //},
                Key = new Dictionary<string, AttributeValue>
                {
                    { "Id", new AttributeValue { N = id.ToString() } },
                    //{ "lastname", new AttributeValue { S = lastname } }
                },
            };
            var result = dynamoclient.GetItemAsync(request).Result;

            var response = dynamoclient.GetItemAsync(
                    tableName: "SummitCustomer", //"gracecustomer",
                    key: new Dictionary<string, AttributeValue>
                    {
                        {"Id", new AttributeValue {N = id.ToString()}}
                    }
                ).Result;
            var actual = response.Item;

            // Process the result.
            var dtid = Int32.Parse(actual["DTID"].N);
            var lastName = actual["LastName"].S;
            var firstName = actual["FirstName"].S;
            var customerid = Int32.Parse(actual["Id"].N);
            var address = actual["Address"].M;
            var addr1 = address["Addr1"].S;
            var city = address["City"].S;
            var state = address["State"].S;
            var zip = address["Zip"].S;
            var contacts = actual["Contacts"].M;  // this doesn't work - how do you get contacts out? it's not recognized as a valid key
            var emails = contacts["Email_addresses"].SS ?? new List<string>();
            var mobile = contacts.ContainsKey("Mobile") ? (contacts["Mobile"].S ?? string.Empty) : string.Empty;
            var homephone = contacts.ContainsKey("Homephone") ? (contacts["Homephone"].S ?? string.Empty) : string.Empty;
            var workphone = contacts.ContainsKey("Workphone") ? (contacts["Workphone"].S ?? string.Empty) : string.Empty;
            var employer = contacts.ContainsKey("Employer") ? (contacts["Employer"].S ?? string.Empty) : string.Empty;

            var customerObj = new Customer
            {
                LastName = lastName,
                FirstName = firstName,
                Id = customerid,
                DTID = dtid,
                Address1 = addr1,
                City = city,
                State = state,
                Zip = zip,
                EmailAddresses = emails,
                MobilePhone = mobile,
                WorkPhone = workphone,
                HomePhone = homephone,
                Employer = employer
            };
            return JsonConvert.SerializeObject(customerObj); //response.Item);
        }

        private static string GetCustomerByLastName(string lastname) // TODO, use query instead of scan next time
        {
            var customerByIdRequest = new ScanRequest
            {
                TableName = "SummitCustomer", //"gracecustomer",
                // Optional parameters.
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> {
                    {
                        ":lastname", new AttributeValue { S = lastname }
                    }
                },
                FilterExpression = "LastName = :lastname",
                ProjectionExpression = "Id,DTID,FirstName,LastName"
            };

            var item = dynamoclient.ScanAsync(customerByIdRequest).Result;
            // item is only a scan response. We need to iterate over item.Items to get our actual result. Alternately, we could have used query, to return the one item instead
            var actual = item.Items[0];
            // Process the result.
            var dtid = Int32.Parse(actual["DTID"].N);
            var lastName = actual["LastName"].S;
            var firstName = actual["FirstName"].S;
            var customerid = Int32.Parse(actual["Id"].N);

            var customerObj = new Customer  // remember, our projectionExpression only listed these 4 properties
            {
                LastName = lastName,
                FirstName = firstName,
                Id = customerid,
                DTID = dtid,
            };
            return JsonConvert.SerializeObject(customerObj); //item);
        }

        private static void CreateRowLevelPermissions()
        {
            JArray permissionArray;
            using (TextReader tr = File.OpenText("rowpermissionspolicy.json"))
            {
                using (JsonTextReader jtr = new JsonTextReader(tr))
                {
                    permissionArray = (JArray)JToken.ReadFrom(jtr);
                }
            }
            foreach (var item in permissionArray)
            {
                string itemJson = item.ToString();
                Document doc = Document.FromJson(itemJson);
                // now what?
            }
        }

        #endregion project.json and user secrets way

        #region appsettings.json way
        private static void CreateDynamoTable()
        {
            // First, set up a DynamoDB client for DynamoDB Local
            AmazonDynamoDBConfig ddbConfig = new AmazonDynamoDBConfig();
            ddbConfig.ServiceURL = "http://localhost:5555";

            try
            {
                AmazonDynamoDBClient client;
                using (client = new AmazonDynamoDBClient(ddbConfig))
                {
                    // Build a 'CreateTableRequest' for the new table
                    CreateTableRequest createRequest = new CreateTableRequest
                    {
                        TableName = "SummitCustomer",
                        AttributeDefinitions = new List<AttributeDefinition>()
                                {
                                    new AttributeDefinition
                                    {
                                    AttributeName = "Id",
                                    AttributeType = "N"
                                    },
                                    new AttributeDefinition
                                    {
                                    AttributeName = "DTID",
                                    AttributeType = "N"
                                    },
                                    new AttributeDefinition
                                    {
                                    AttributeName = "LastName",
                                    AttributeType = "S"
                                    },
                                    new AttributeDefinition()
                                    {
                                        AttributeName = "FirstName",
                                        AttributeType = "S"
                                    },
                                },
                        KeySchema = new List<KeySchemaElement>()
                                {
                                    new KeySchemaElement
                                    {
                                        AttributeName = "Id",
                                        KeyType = "HASH"  // means partition key
                                    },
                                    new KeySchemaElement
                                    {
                                    AttributeName = "DTID",
                                    KeyType = "RANGE"   // means sort key
                                    }
                                },
                    };

                    // Provisioned-throughput settings are required even though
                    // the local test version of DynamoDB ignores them
                    createRequest.ProvisionedThroughput = new ProvisionedThroughput(1, 1);

                    // Using the DynamoDB client, make a synchronous CreateTable request
                    CreateTableResponse createResponse;
                    try
                    {
                        createResponse = client.CreateTableAsync(createRequest).Result; // creates on dynamo server locally
                                                                                        //createResponse = ddclient.CreateTableAsync(createRequest).Result; // creates on aws 

                        // Report the status of the new table...
                        Console.WriteLine(
                            "\n\n Created the \"Customer\" table successfully!\n    Status of the new table: '{0}'",
                            createResponse.TableDescription.TableStatus);


                        // Keep the console open if in Debug mode...
                        Console.Write("\n\n ...Press any key to continue");
                        Console.ReadKey();
                        Console.WriteLine();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("\n Error: failed to create the new table; " + ex.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("\n Error: failed to create client for dynamodb; " + e.Message);
            }

        }

        private static void ImportTable()
        {
            // First, read in the JSON data from the moviedate.json file
            JArray customerArray = null;
            try
            {

                //using (StreamReader sr = new StreamReader("customerdata.json"))  // doesnt want to compile
                using (System.IO.TextReader tr = File.OpenText("customerdata.json")) //wtf??
                {
                    using (JsonTextReader jtr = new JsonTextReader(tr))
                    {
                        customerArray = (JArray)JToken.ReadFrom(jtr);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n Error: could not read from the 'customerdata.json' file, because: " + ex.Message);
                Console.WriteLine("Aborting program.");
            }
           

            // Get a Table object for the table that you created in Step 1
            Table table = GetTableObject("SummitCustomer");
            if (table == null)
                Console.WriteLine("Aborting program.");

            // Load the customer data into the table (this could take some time)
            Console.Write("\n   Now writing {0:#,##0} customer records from customerdata.json (might take 15 minutes)...\n   ...completed: ", customerArray.Count);
            for (int i = 0, j = 99; i < customerArray.Count; i++)
            {
                try
                {
                    string itemJson = customerArray[i].ToString();
                    Document doc = Document.FromJson(itemJson);
                    table.PutItemAsync(doc);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("\nError: Could not write the customer record #{0:#,##0}, because {1}", i, ex.Message);
                    Console.WriteLine("Aborting program.");
                }
                if (i >= j)
                {
                    j++;
                    Console.Write("{0,5:#,##0}, ", j);
                    if (j % 1000 == 0)
                        Console.Write("\n                 ");
                    j += 99;
                }
            }
            Console.WriteLine("\n   Finished writing all customer records to DynamoDB!");
        }
    

        private static Table GetTableObject(string tableName)
        {
            try
            {
                // First, set up a DynamoDB client for DynamoDB Local
                AmazonDynamoDBConfig ddbConfig = new AmazonDynamoDBConfig();
                ddbConfig.ServiceURL = "http://localhost:55555";
                using (AmazonDynamoDBClient client = new AmazonDynamoDBClient(ddbConfig))
                {
                    // Now, create a Table object for the specified table
                    Table table;
                    try
                    {
                        table = Table.LoadTable(client, tableName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("\n Error: failed to load the 'Customer' table; " + ex.Message);
                        return (null);
                    }
                    return (table);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n Error: failed to create a DynamoDB client; " + ex.Message);
                return (null);
            }
        }
        #endregion appsettings method
    }

    public class Customer
    {
        public int Id { get; set; }
        public int DTID { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string Address1 { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }

        public List<string> EmailAddresses { get; set; }
        public string MobilePhone { get; set; }
        public string HomePhone { get; set; }
        public string WorkPhone { get; set; }
        public string Employer { get; set; }
    }

}