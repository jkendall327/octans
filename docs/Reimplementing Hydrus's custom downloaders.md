# Reimplementing Hydrus's custom downloaders in C#

Goals:
- Nothing hardcoded in the app itself
- Flexible enough to cover wildly different sites
- Simple way to install and distribute custom downloaders

Plan:
Use MoonSharp to run Lua from files in C#.
This lets users write their own downloader logic in Lua and the Octans app will run it.
We should try to sandbox the Lua code somewhat to prevent it from doing abjectly evil things (like deleting the user's harddrive).

Users will package their custom downloaders as a simple folder which contains Lua text files.
The text files will adhere to set patterns ('gug.lua', 'parser.lua', etc.) which Octans will look for.

# URL Classes

Implement a flexible URL matching and classification system similar to Hydrus
Support different URL types: File URL, Post URL, Gallery URL, and Watchable URL
Allow for URL normalization and parameter handling

Example Lua for a URL classifier:

```
function match_url(url)
    -- Implement matching logic here
    -- Return true if matched, false otherwise
end

function classify_url(url)
    -- Implement classification logic here
    -- Return a table with classification details
    return {
        type = "post", -- or "file", "gallery", "watchable"
        domain = "example.com",
        id = "12345",
        -- other relevant fields
    }
end

function normalize_url(url)
    -- Implement normalization/canonicalization logic here
    -- Return normalized URL string
end
```

# Parsers

Create a modular parser system with support for HTML and JSON parsing
Implement formula objects for extracting specific data from parsed content
Develop content parsers to apply metadata types to extracted strings
Build page parsers to organize multiple content parsers and handle complex page structures

```
function parse_html(html_content)
    local download_urls = {}

    -- TODO: Implement HTML parsing logic here
    -- Add extracted URLs to download_urls table

    return download_urls
end

function parse_json(json_content)
    local download_urls = {}

    -- TODO: Implement JSON parsing logic here
    -- Add extracted URLs to download_urls table

    return download_urls
end

```

The Lua interface should also require methods for things like:

*   The image URL.
*   The different tags and their namespaces.
*   The secret md5 hash buried in the HTML.
*   The post time.
*   The source URL.

# Gallery URL Generators (GUGs)

Implement a system to convert user input into initializing Gallery URLs
Support nested GUGs for handling multiple streams from a single input

```
-- Metadata for the GUG
GUG = {
    name = "Example Booru Search",
    initial_search_text = "Enter tags, separated by spaces",
    example_searches = {"blue_eyes", "landscape sunset", "character:samus_aran"},
    applicable_url_types = {"gallery"}
}

-- Function to validate user input
-- Returns true if input is valid, false otherwise
function validate_input(input)
    -- Implement input validation logic here
    -- Example: Check if input contains only allowed characters
    return input:match("^[%w%s_:]+$") ~= nil
end

-- Main function to generate the Gallery URL
-- Parameters:
--   input: string - The user's search input
--   page: number - The page number (starting from 1)
-- Returns:
--   string - The generated Gallery URL
function generate_url(input, page)
    -- Validate input
    if not validate_input(input) then
        error("Invalid input: " .. input)
    end

    -- Preprocess input
    local processed_input = preprocess_input(input)

    -- TODO: Implement URL generation logic here
    -- Example:
    local base_url = "https://example-booru.com/posts"
    local tags_param = "tags=" .. processed_input
    local page_param = "page=" .. page

    return base_url .. "?" .. tags_param .. "&" .. page_param
end
```

# API Integration

For sites which offer dedicated APIs for getting content, meaning we don't need to scrape them.
To enable API support you just include an 'api.lua' file in your directory.

```
API = {
    name = "Example Site API",
    version = "1.0",
    description = "API interface for ExampleSite.com",
    base_url = "https://api.examplesite.com/v1/",
    requires_auth = false, -- Set to true if the API requires authentication
    rate_limit = 60 -- Requests per minute, nil if no rate limit
}

-- Optional: Authentication function
-- Implement this if requires_auth is true
-- Returns: table with authentication details (e.g., headers, tokens)
function authenticate()
    -- TODO: Implement authentication logic here
    -- Example:
    -- return {
    --     headers = {
    --         ["Authorization"] = "Bearer " .. get_access_token()
    --     }
    -- }
end

-- Main function to process a user query and return download URLs
-- Parameters:
--   query: string - The user's search query
--   page: number - The page number (starting from 1)
--   options: table - Additional options (e.g., filters, sort order)
-- Returns:
--   table - A list of tables, each containing:
--     url: string - The raw download URL
--     metadata: table - Additional metadata for the file (optional)
function process_query(query, page, options)
    local results = {}

    -- TODO: Implement API query logic here
    -- Example:
    local api_url = API.base_url .. "search?q=" .. url_encode(query) .. "&page=" .. page

    local response = make_api_request(api_url)

    -- Parse the API response and extract download URLs and metadata
    for _, item in ipairs(response.items) do
        table.insert(results, {
            url = item.download_url,
            metadata = {
                title = item.title,
                author = item.author,
                tags = item.tags
            }
        })
    end

    return results
end

function make_api_request(url)

    -- Placeholder implementation
    return {
        items = {
            {
                download_url = "https://example.com/file1.jpg",
                title = "Example File 1",
                author = "John Doe",
                tags = {"tag1", "tag2"}
            },
            {
                download_url = "https://example.com/file2.jpg",
                title = "Example File 2",
                author = "Jane Doe",
                tags = {"tag2", "tag3"}
            }
        }
    }
end

-- Optional: Function to validate and preprocess the user's query
function preprocess_query(query)
    -- TODO: Implement query preprocessing logic
    -- Example: Remove unsupported characters, add default tags, etc.
    return query:gsub("[^%w%s]", "")
end
```