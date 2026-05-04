import server


def test_build_content_disposition_unicode():
    h = server._build_content_disposition('中文文件.txt')
    assert "filename*=" in h
    assert "UTF-8''" in h


if __name__ == "__main__":
    test_build_content_disposition_unicode()
    print("ok")

