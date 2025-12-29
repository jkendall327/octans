from playwright.sync_api import sync_playwright
import time

def verify_archive_features():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()

        try:
            # Wait for server to start
            time.sleep(10)

            # 1. Navigate to Gallery (use port 5229 as seen in log)
            page.goto("http://localhost:5229/gallery")
            # Wait for gallery container
            page.wait_for_selector(".gallery-container")

            # Take screenshot of gallery
            page.screenshot(path="/home/jules/verification/gallery_initial.png")

            # 2. Check Import page for Auto Archive toggle
            page.goto("http://localhost:5229/import")
            # Wait for the toggle label
            page.wait_for_selector("text=Auto Archive")

            page.screenshot(path="/home/jules/verification/import_auto_archive.png")

        except Exception as e:
            print(f"Error: {e}")
        finally:
            browser.close()

if __name__ == "__main__":
    verify_archive_features()
