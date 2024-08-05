# Hydrus reimplementation notes

## Statement of purpose

* Subset of Hydrus Network written in C\#  
* Goals: relative simplicity, performance, power-user capabilities, highly modular design to avoid tech debt  
* Anti-goals: 1:1 parity with Hydrus, high levels of polish

## Architecture

Two main binaries: server and client.  
Both implemented as ASP.NET Core web apps.

Server is solely concerned with:  
\- importing files from on-disk  
\- storing files and their metadata in database  
\- retrieving files and metadata for queries by client

Client is a modular monolith that handles 'everything else', i.e.  
\- booru style web page viewing  
\- ripping  
\- parsing  
\- background jobs

import jobs etc. (anything long-running) should return 200 OK on valid requests and return a job id. which you can then query another endpoint with to see the results of that job.

maybe the client also exposes an endpoint that the API can fire a message off to as a callback when the work is done. to avoid polling. Client endpoint can then use mediatr to inform other components

## Roadmap

1. Get server component running that solely handles importing, file persistence and file retrieval  
2. A basic MVP of the web UI for querying files in the client  
3. Get raw url downloads working with appropriate hashing and saving  
4. Get url downloaders working  
5. Get gallery downloaders working, leveraging the work of url downloaders  
6. Other stuff as desired (subscriptions, blacklists, whatever)

Why this order?

Server component will be an isolated binary that changes very little. A bedrock that everything else works against for stability’s sake. Microservices ideal. Its functionality (importing, hashing) is also fundamental to everything else.

Raw URL downloads are a small addition on top of that — essentially implementing the networking stack, but nothing complex.

Post URL downloaders are an incremental additions that builds on top of that again. Requires implementing parsing, scraping, API url redirects. This will be the biggest addition and probably require heavy cribbing from Hydrus’s implementation.

Gallery URL downloaders are the final incremental addition — essentially just GUGs on top of post URL downloaders.

## Basic importing

hashing: [https://stackoverflow.com/a/73126261](https://stackoverflow.com/a/73126261)

The filesystem storage:

- Made in db/files  
- Has separate folders for files (‘f’) and thumbnails (‘t’)  
- How does it decide which folders to make?  
- How does it decide where to put a file?  
- Does anything get saved alongside the file?  
- What gets put in the database and when?

[ClientFiles.py/AddFile()](http://ClientFiles.py/AddFile()):

- Takes a read-write lock on the masterfiles location  
- Takes a lock on the ‘f’ subfolders  
- Takes a lock on the ‘t’ subfolders if bytes for a thumbnail are provided  
- Calls smaller methods for file and thumbnail

[ClientFiles.py/\_AddFile()](http://ClientFiles.py/\_AddFile()):

- Takes the hash of the file, its MIME, its source path  
- Generates the expected new filepath via the hash and the MIME (this is the only use of the MIME):  
  - See below  
- Checks if there is free space before copying, errors otherwise  
- Calls HydrusPaths.MirrorFile() for the actual copying  
- That method does basic sanity-checking:  
  - Does source exist, does destination not exist  
  - Are either of them directories?  
  - If both exist, do they have the same size/date  
  - If so can choose to do nothing and reports that back  
  - Eventually just copies it over in a very safe/paranoid way

[ClientFiles.py/\_GenerateExpectedFilePath()](http://ClientFiles.py/\_GenerateExpectedFilePath()):

- Takes the hash of the file and its MIME  
- Gets expected subdir by passing ‘f’ and the hash down to another method  
  - Generates a ‘prefix umbrella’, sees if that maps onto an existing subfolder in the client files, and returns it if so  
  - Prefix umbrella is: prefix\_type \+ hash.hex()\[ : self.\_shortest\_prefix \], where prefix\_type is ‘f’ for files and ‘t’ for thumbnails. I.e. it gets the first 2 chars of the encoded hash.  
- Then encodes the hash? Is this the 32-bit/64-bit thing? hash.hex()  
- Once it knows the subfolder: subfolder.GetFilePath( f'{hash\_encoded}{HC.mime\_ext\_lookup\[ mime \]}' )

Mapping MIME types to extensions: search for mime\_ext\_lookup in HydrusConstants.py  
How it determines MIME types: search for headers\_and\_mime in HydrusfileHandling.py

See [ClientFiles.py/\_GenerateThumbnailBytes()](http://ClientFiles.py/\_GenerateThumbnailBytes()) for thumbnail generation

[ClientFiles.py/\_Reinit()](http://ClientFiles.py/\_Reinit()) handles setting up its folders, including subfolders. Will attempt to regenerate any subfolders it thinks are missing. The \_ReinitSubfolders() method here is interesting. WTF is self.\_controller.Read( 'client\_files\_subfolders' )? Seems to be an insane way of reading a DB value/table?

No, it’s a stringly-typed call to GetClientFilesSubfolders() in ClientDBFilesPhysicalStorage.py. Which really just runs 'SELECT prefix, location, purge FROM client\_files\_subfolders;'

But it also determines missing prefixes and inserts them if they’re not there.

[ClientFilesPhysical.py/GetMissingPrefixes()](http://ClientFilesPhysical.py/GetMissingPrefixes()) seems like a key for deciding what gets made. Just makes a subfolder for every instance of the Cartesian join of the hexadecimal values (0123456789abcdef).

Cool, that makes sense. Now how does it decide where to put a file? Based on the first two characters of its hash, duh. Because we’re doing the Cartesian join we know the matching folder should always exist.

So, what gets saved to the database?

[ClientDb.py/\_ImportFile()](http://ClientDb.py/\_ImportFile()) is the actual logic called:

- Is given a FileImportJob which contains the hash generated pre-import  
- Gets the ‘hash ID’ to check if the file has already been imported  
- Adds the file’s metadata (size, mime, height, width etc.) to the FilesInfo table, with the hash ID as the foreign key  
- Gets and sets extra hashes for some reason \- md5, sha1, sha512  
- Sets various metadata for the file (transparency, has EXIF etc)  
- Calls self.\_AddFiles( destination\_service\_id, \[ ( hash\_id, now\_ms ) \] )  
- Does the actual SQL nonsense after determining what delta needs to go in  
- There are current\_files\_1, current\_files\_2 etc. tables for each service (service ID 1, service ID 2 etc.)  
- It has separate tables for ‘pending\_files’ and ‘current\_files’ \- safety presumably. Though shouldn’t SQLite being ACID handle this?

### Tags

- How do tags get saved?

TestClientDbTags.py may be a rosetta stone…

CREATE TABLE tags ( tag\_id INTEGER PRIMARY KEY, namespace\_id INTEGER, subtag\_id INTEGER );  
CREATE TABLE namespaces ( namespace\_id INTEGER PRIMARY KEY, namespace TEXT UNIQUE );  
CREATE TABLE subtags ( subtag\_id INTEGER PRIMARY KEY, subtag TEXT UNIQUE );

‘Subtag’ refers to the portion after the namespace.

Unnamespaced tags are implemented just as tags with the namespace “”.

Parent tags are implemented just by a row in tag\_parents:

CREATE TABLE tag\_parents ( service\_id INTEGER, child\_tag\_id INTEGER, parent\_tag\_id INTEGER, status INTEGER, PRIMARY KEY ( service\_id, child\_tag\_id, parent\_tag\_id, status ) );

No idea what ‘status’ is.

Siblings are implemented pretty much identically.

So the magic must be in how they’re retrieved… a join somewhere.

How does the link between a hash and its tags get stored? There is a ‘current\_mappings\_{serviceId}’ table in the dedicated mappings database. This is just a straight many-many joining table between hash IDs and tag IDs.

### Pending/current files

Pending seems to refer to tags that are yet to be uploaded to some remote. Not an ACID thing. Think we can ignore this.

### Hash Ids

A file has a sha256 hash.

[ClientDBDefinitionsCache.Py/GetHashId()](http://ClientDBDefinitionsCache.Py/GetHashId()):  
'SELECT hash\_id FROM local\_hashes\_cache WHERE hash \= ?;', ( sqlite3.Binary( hash ),)

If not in cache it calls: 'SELECT hash\_id FROM hashes WHERE hash \= ?;', ( sqlite3.Binary( hash ), )

If not there it inserts it: 'INSERT INTO hashes ( hash ) VALUES ( ? );', ( sqlite3.Binary( hash ), )

The database schema:  
CREATE TABLE hashes ( hash\_id INTEGER PRIMARY KEY, hash BLOB\_BYTES UNIQUE );

## parsing

hydrus has three url types: post url (individual post), gallery url (many posts), watchable url (self-updating gallery?)

url class links-\>api/redirect link preview screen  
also shows that for recognised links it will use established apis instead of scraping. are these apis configured anywhere or hardcoded?

calling apis is the same to hydrus as scraping, they're all just parsing.

downloaders-as-data  
how would this work?  
just copy the hydrus model directly  
aim for compatability with hydrus parsers/importers

gallery url generators \-\> get images from gallery page \-\> download image page

Our parser needs to be generic enough to handle dlsite (or special-cased for it).

## database design

tags are just a serial number, which has a separate link to a table with the human-readable name  
how are parents and siblings implemented?

it uses multiple databases so there's redundancy \- they can regenerate one from the other.

## Resources

[https://github.com/hydrusnetwork/hydrus/tree/bbfe8e4b206617dca5bf087c9247e7c4a0434764/hydrus/client/networking](https://github.com/hydrusnetwork/hydrus/tree/bbfe8e4b206617dca5bf087c9247e7c4a0434764/hydrus/client/networking)

[https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing?tabs=dotnet-core-cli](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing?tabs=dotnet-core-cli)