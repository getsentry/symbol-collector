#!/usr/bin/env python
# -*- coding: utf-8 -*-

"""search osx/macos update packages and get result as json including title and download-url of every matching package"""

import json
import re
import requests
import sys


def search(q):

    items = []

    for n in range(17):
        try:
            # GET https://support.apple.com/kb/index...
            response = requests.get(
                url="https://support.apple.com/kb/index",
                params={
                    "page": "downloads_search",
                    # max offset seems to be 16, seems to have no correllation with "totalresults" value in response.text 
                    "offset": n,
                    "sort": "relevancy",
                    "facet": "all",
                    "category": "",
                    "q": q,
                    "locale": "en_US",
                },
            )
            # print('Response HTTP Status Code: {status_code}'.format(status_code=response.status_code))
            # print('Response HTTP Response Body: {content}'.format(content=response.content))
		
        except requests.exceptions.RequestException:
            print('HTTP Request failed', "https://support.apple.com/kb/index")

        items += json.loads(response.text).get("downloads", [])

    result = {
		"count": len(items),
		"packages": [],
	}
    for item in items:
		detail_body = get_detail_body(item["url"])

		result["packages"].append({
			"title": item.get("title", "<none>"),
			"url": get_dmg(detail_body),
		})
    return result

def get_detail_body(url):

    try:
        # GET https://support.apple.com/kb/DL661...
        response = requests.get(
            url=url,
            # NOTE: those params are and should be part of the url
            # params={
            #     "viewlocale": "en_US",
            #     "locale": "en_US",
            # }
        )
        # print('Response HTTP Status Code: {status_code}'.format(status_code=response.status_code))
        # print('Response HTTP Response Body: {content}'.format(content=response.content))
        return response.text
    except requests.exceptions.RequestException:
        print('HTTP Request failed', url)



def get_dmg(res_body):
    # extracts dmg url out of detail page html body
    # res_body example: ...."metaUrl": "https://download.info.apple.com/Mac_OS_X/061-0355.20030213.swp57/2Z/MacOSX10.2.4CombinedUpd.dmg.bin",...
	regex = r".*\"metaUrl\": \"(https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*))\","
	return re.search(regex, res_body, re.MULTILINE).group(1)

def main():
    result = search(sys.argv[1])
    print(json.dumps(result, indent=1))


if __name__ == "__main__":
    main()
