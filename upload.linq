<Query Kind="FSharpProgram">
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.Http.dll</Reference>
  <NuGetReference>FSharp.Core</NuGetReference>
  <NuGetReference>FSharp.Data</NuGetReference>
  <NuGetReference>Newtonsoft.Json</NuGetReference>
  <Namespace>FSharp.Data</Namespace>
  <Namespace>FSharp.Data.Runtime</Namespace>
  <Namespace>FSharp.Data.Runtime.BaseTypes</Namespace>
  <Namespace>FSharp.Data.Runtime.StructuralTypes</Namespace>
  <Namespace>FSharp.Data.Runtime.WorldBank</Namespace>
  <Namespace>Newtonsoft.Json</Namespace>
  <Namespace>Newtonsoft.Json.Converters</Namespace>
</Query>

open System
open System.IO
open System.Net.Http
open System.Security.Cryptography
open FSharp.Data
open FSharp.Data.JsonExtensions
open Newtonsoft.Json.Converters
open System.Threading
open System.Linq

let musicDir = @"E:\Music"
let email = "email@domain.com"
let password = "passwd"

///http://www.fssnip.net/fy/title/Typeinference-friendly-division-and-multiplication
/// Floating point division given int and int args.
let (./.) x y = 
    (x |> double) / (y |> double)
    
//wrappers

let replace oldStr newStr (s : string) = 
  s.Replace(oldValue=oldStr, newValue=newStr)

let toLower (s : string) = s.ToLower() 

let getDirectory dir lastModified = 
    (new DirectoryInfo(dir)).EnumerateFiles("*.*", SearchOption.AllDirectories) 
    //|> Seq.filter (fun d -> d.LastWriteTime > lastModified)

let pFilter p (s : seq<'T>) =
    ParallelEnumerable.Where(s.AsParallel(), Func<_,_>(p))
    
//end wrappers

//LinqPad Dump Stuff

let dc = new DumpContainer()
dc.Dump()
let mutable count = 0
let mutable total = 0
let dump s = 
    dc.Content <- s
    Util.Progress <- System.Nullable (int ((count ./. total)*100.0))
    
//end LinqPad Dump Stuff

//types

type Body = {
    mode: string
    email_address: string
    password: string
    version: int
    client: string
    supported_types: int
    device_name: string
} 

type ibroadcast = {
    UserId:string; 
    Token:string; 
    Checksums:seq<string>; 
    Supported:seq<string>
}

//end types
let clientName = "f# uploader script"

let getMD5HashFromFile (file : FileInfo) =
    use fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024 * 1024)
    let md5 = MD5.Create()
    md5.ComputeHash fileStream
        |> BitConverter.ToString
        |> replace "-" ""
        |> toLower

let loadMediaFiles dir ( extensions: seq<string> ) lastModified =
    getDirectory dir lastModified
    |> Seq.filter (fun e -> extensions.Contains(e.Extension.ToLower()))

let uploadFile (url: string) userId token (file: FileInfo) = async {
    let filestream = new FileStream(file.FullName, FileMode.Open)
    let client = new HttpClient()

    let content = new MultipartFormDataContent()
    content.Add(new StringContent(userId), @"""user_id""")
    content.Add(new StringContent(token), @"""token""")
    content.Add(new StringContent(file.FullName), @"""file_path""")
    content.Add(new StringContent(clientName), @"""method""")
    content.Add(new StreamContent(filestream), @"""file""", sprintf @"""%s""" file.FullName)

    let result = client.PostAsync(url, content)
    let! response = Async.AwaitTask result
    let! content = Async.AwaitTask (response.Content.ReadAsStringAsync())
    return content
}


let uploadFiles (files : seq<FileInfo>) userId token (checksums:seq<string>) =
    let contains md checksums = Seq.exists (fun elem -> elem = md) checksums
    count <- 0
    total <- Seq.length files
    let isMissing file = 
            let md = getMD5HashFromFile file
            count <- count + 1
            dump (sprintf "%s\n (%d of %d)" file.FullName count total)
            not (contains md checksums)
    let missing = files |> pFilter isMissing |> List.ofSeq
    count <- 0
    total <- missing.Length
    [for file in missing -> 
        count <- count + 1
        dump (sprintf "uploading: %s\n (%d of %d)" file.FullName count total)
        let result = uploadFile "https://sync.ibroadcast.com" userId token file |> Async.RunSynchronously
        printfn "%A %s: %A" DateTime.Now file.FullName result
        ]
    //|> Async.Parallel    
 
let body = {
    mode = "status";
    email_address = email;
    password = password;
    version = 1;
    client = clientName;
    supported_types = 1;
    device_name = "LINQPad";    
}

let login = async {
    dump "Logging in..."
    let! value = Http.AsyncRequestString("https://json.ibroadcast.com/s/JSON/", headers = [ HttpRequestHeaders.ContentType HttpContentTypes.Json ], body = TextRequest (JsonConvert.SerializeObject body))
    let json = JsonValue.Parse(value)
    let supported = json?supported.AsArray() |> Seq.map (fun e -> e?extension.AsString())    
    let token = json?user?token.AsString()    
    let userId = json?user?id.AsString()
    //.AsDateTime() parses as DateTimeKind.Local
    //we need DateTimeKind.UTC
    let lastModified = DateTime.Parse(json?status?lastmodified.AsString()).ToLocalTime()
    return (supported, token, userId, lastModified)
} 

let getMD5List userId token = async {
    dump "Getting uploaded tracks..."
    let! value = Http.AsyncRequestString("https://sync.ibroadcast.com", body = FormValues ["user_id", userId;"token", token])
    return JsonValue.Parse(value)?md5.AsArray()
        |> Seq.map (fun elem -> elem.AsString())
}

let (supported, token, userId, lastModified) = login |> Async.RunSynchronously
let checksums = getMD5List userId token |> Async.RunSynchronously

uploadFiles (loadMediaFiles musicDir supported lastModified) userId token checksums |> ignore
